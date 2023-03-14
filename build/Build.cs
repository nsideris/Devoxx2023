using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nuke.Common;
using Nuke.Common.Execution;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.Docker;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.Kubernetes;
using LogLevel = Nuke.Common.LogLevel;

class Build : NukeBuild
{
    public static int Main() => Execute<Build>(x => x.BlueGreenDeploy);

 
    [Solution] readonly Solution Solution;

    string Version = "1.0.6";

    [Parameter] [Secret] readonly string DockerPassword;
    [Parameter] [Secret] readonly string AzureUserName;
    [Parameter] [Secret] readonly string AzurePassword;
    [Parameter] [Secret] readonly string AzureTenantId;
    [Parameter] [Secret] readonly string AzureSubscription;

    Target Restore => _ => _
        .Executes(() =>
        {
            DotNetTasks.DotNetClean(s => s.SetProject(Solution));
            DotNetTasks.DotNetRestore(s => s.SetProjectFile(Solution));
            DotNetTasks.DotNetToolRestore();
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetTasks.DotNetBuild(s => s.EnableNoRestore().SetProjectFile(Solution));
        });

    string DockerTagName;

    Target BuildDockerImage => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            var dotNetPublish = DotNetTasks.DotNetPublish(s =>
                s
                    .SetProject(Solution)
                    .SetRuntime("linux-x64")
                    .SetPublishProfile("DefaultContainer")
                    .SetVersion(Version)
            );
            DockerTagName = dotNetPublish.Last().Text.Split("'")[1];
        });

    Target PushDockerImage => _ => _
        .DependsOn(BuildDockerImage)
        .Executes(() =>
        {
            DockerTasks.DockerLogin(s => s.SetUsername("9412036").SetPassword(DockerPassword));
            DockerTasks.DockerTag(s => s.SetSourceImage(DockerTagName).SetTargetImage($"9412036/{DockerTagName}"));
            DockerTasks.DockerPush(s => s.SetName($"9412036/{DockerTagName}"));
        });


    Target LoginAzurePrincipal => _ => _.DependsOn(PushDockerImage).Executes(() =>
    {
        ProcessTasks.StartProcess("az",
                $"  login --service-principal -u \"{AzureUserName}\"  --password \"{AzurePassword}\"  --tenant \"{AzureTenantId}\" ")
            .WaitForExit();
        var cluster = "dev";
        var context = "dev";
        var command = "aks get-credentials --overwrite-existing --subscription ";
        command += $"\"{AzureSubscription}\" ";
        command += $" --resource-group k8s-{cluster} --name {context}  -a";

        ProcessTasks.StartProcess("az", command).WaitForExit();
    });

    AbsolutePath K8SYamlFiles => RootDirectory / "k8s";


    DeploymentEnvironment ProductionCandidateEnvironment;

    Target DeployInactiveEnvironment => _ => _
        .DependsOn(LoginAzurePrincipal)
        .Executes(() =>
        {
            var activeServiceK8SDescribeResults =
                KubernetesTasks.KubernetesDescribe(s => s.SetFilename($"{K8SYamlFiles}/service.yaml"));
            var activeEnvironment = activeServiceK8SDescribeResults
                .SingleOrDefault(x => x.Text.Contains("Selector: ")).Text
                .Split(',').SingleOrDefault(x => x.Contains("role"))
                ?.Split('=').Last().ToDeploymentEnvironment();
            ProductionCandidateEnvironment = activeEnvironment.Value.GetOppositeEnvironment();

            //Deploy Service Test
            var replaceDict = new Dictionary<string, string>
            {
                {"TARGET_ROLE", ProductionCandidateEnvironment.ToString().ToLower()},
                {"VERSION", Version}
            };

            //Replace Text
            DeploymentEnvironmentExtensions.ReplaceFileWithCommonParameters($"{K8SYamlFiles}/serviceTest.yaml",
                $"{K8SYamlFiles}/serviceTest.yaml",
                replaceDict);
            DeploymentEnvironmentExtensions.ReplaceFileWithCommonParameters($"{K8SYamlFiles}/deployment.yaml",
                $"{K8SYamlFiles}/deployment.yaml",
                replaceDict);


            //Deploy
            KubernetesTasks.KubernetesApply(s => s
                .AddFilename($"{K8SYamlFiles}/serviceTest.yaml"));
            KubernetesTasks.KubernetesApply(s => s
                .AddFilename($"{K8SYamlFiles}/deployment.yaml"));
        });

    Target RunTests => _ => _
        .DependsOn(DeployInactiveEnvironment)
        .Executes(() =>
        {
            //K6 Tests
        });

    Target SwitchRouting => _ => _.DependsOn(RunTests).Executes(() =>
    {
        var replaceDict = new Dictionary<string, string>
        {
            {"TARGET_ROLE", ProductionCandidateEnvironment.ToString().ToLower()},
            {"CLUSTER", "Dev"}
        };
        //Deploy Service that should Target the inactive environment
        DeploymentEnvironmentExtensions.ReplaceFileWithCommonParameters($"{K8SYamlFiles}/service.yaml",
            $"{K8SYamlFiles}/service.yaml",
            replaceDict);
        //Deploy
        KubernetesTasks.KubernetesApply(s => s
            .AddFilename($"{K8SYamlFiles}/service.yaml"));
    });

    Target BlueGreenDeploy => _ => _.DependsOn(SwitchRouting).Executes(() =>
    {
        Logging.Level = LogLevel.Warning;
    });
}

public enum DeploymentEnvironment
{
    Green,
    Blue
}

public static class DeploymentEnvironmentExtensions
{
    public static DeploymentEnvironment ToDeploymentEnvironment(this string deploymentEnvString)
    {
        return Enum.Parse<DeploymentEnvironment>(char.ToUpper(deploymentEnvString[0]) + deploymentEnvString[1..]);
    }

    public static DeploymentEnvironment GetOppositeEnvironment(this DeploymentEnvironment environment)
    {
        return environment switch
        {
            DeploymentEnvironment.Blue => DeploymentEnvironment.Green,
            DeploymentEnvironment.Green => DeploymentEnvironment.Blue,
            _ => throw new ArgumentOutOfRangeException(nameof(environment), environment, null)
        };
    }

    public static void ReplaceFileWithCommonParameters(string fileIn, string fileOut,
        Dictionary<string, string> replaceDict)
    {
        var origin = File.ReadAllText(fileIn);

        foreach (var item in replaceDict)
        {
            var search = "${" + item.Key + "}";
            if (origin.Contains(search))
            {
                origin = origin.Replace(search, item.Value);
            }
        }

        File.WriteAllText(fileOut, origin);
    }
}