using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nuke.Common;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.Execution;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.Docker;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.Kubernetes;
using Nuke.Common.Tools.NerdbankGitVersioning;
using Serilog;
using LogLevel = Nuke.Common.LogLevel;

[GitHubActions(
    "continuous",
    GitHubActionsImage.UbuntuLatest,
    On = new[] {GitHubActionsTrigger.Push},
    FetchDepth = 0,
    ImportSecrets = new[]
    {
        "DOCKER_PASSWORD", "AZURE_USER_NAME", "AZURE_PASSWORD", "AZURE_TENANT_ID", "AZURE_SUBSCRIPTION",
        "AZURE_SERVICE_BUS"
    },
    InvokedTargets = new[] {nameof(BlueGreenDeploy)})]
class Build : NukeBuild
{
    public static int Main() => Execute<Build>(x => x.BlueGreenDeploy);


    [Solution]
    readonly Solution Solution;

    [NerdbankGitVersioning] readonly NerdbankGitVersioning NerdbankVersioning;


    Target Restore => _ => _
        .Executes(() =>
        {
            Logging.Level = LogLevel.Normal;
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
            );
            DockerTagName = dotNetPublish.Last().Text.Split("'")[1];
        });

    Target PushDockerImage => _ => _
        .DependsOn(BuildDockerImage)
        .Executes(() =>
        {
            DockerTasks.DockerLogin(s =>
                s.SetUsername("9412036").SetPassword(Environment.GetEnvironmentVariable("DOCKER_PASSWORD")));
            DockerTasks.DockerTag(s => s.SetSourceImage(DockerTagName).SetTargetImage($"9412036/{DockerTagName}"));
            DockerTasks.DockerPush(s => s.SetName($"9412036/{DockerTagName}"));
            Log.Information($"Pushed Docker Image {DockerTagName}");
        });


    Target LoginAzurePrincipal => _ => _.DependsOn(PushDockerImage).Executes(() =>
    {
        ProcessTasks.StartProcess("az",
                $"  login --service-principal -u \"{Environment.GetEnvironmentVariable("AZURE_USER_NAME")}\"  --password \"{Environment.GetEnvironmentVariable("AZURE_PASSWORD")}\"  --tenant \"{Environment.GetEnvironmentVariable("AZURE_TENANT_ID")}\" ")
            .WaitForExit();
        var cluster = "dev";
        var context = "dev";
        var command = "aks get-credentials --overwrite-existing --subscription ";
        command += $"\"{Environment.GetEnvironmentVariable("AZURE_SUBSCRIPTION")}\" ";
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

            Log.Information(
                $"Active Environment is {activeEnvironment} will try deploy to {ProductionCandidateEnvironment}");

            //Deploy Service Test
            var replaceDict = new Dictionary<string, string>
            {
                {"TARGET_ROLE", ProductionCandidateEnvironment.ToString().ToLower()},
                {"VERSION", NerdbankVersioning.NuGetPackageVersion},
                {"SBCS", Environment.GetEnvironmentVariable("AZURE_SERVICE_BUS")}
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

    Target RunK6Tests => _ => _
        .DependsOn(DeployInactiveEnvironment)
        .Executes(() =>
        {
            //K6 Tests
        });

    Target SwitchRouting => _ => _.DependsOn(RunK6Tests).Executes(() =>
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
        KubernetesTasks.KubernetesApply(s => s
            .AddFilename($"{K8SYamlFiles}/ingress-service.yaml"));
        Log.Information(
            $"Active Environment is {ProductionCandidateEnvironment}");
    });

    Target BlueGreenDeploy => _ => _.DependsOn(SwitchRouting).Executes(() =>
    {
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