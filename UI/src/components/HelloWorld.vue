
<template>
	<html lang="en">
	<div class="squares">
		<div class="squareGreen">
			<p style="font-size:50px; color:aliceblue">{{ greenEnv }}</p>
		</div>
		<div class="squareBlue">
			<p style="font-size:50px; color:aliceblue">{{ blueEnv }}</p>
		</div>
	</div>

	</html>
</template>

 
<script>
import { ref, onMounted } from "vue";

export default {
	name: "HelloWorld",
	props: {},
	setup() {

		const response = ref("");
		const blueEnv = ref(0);
		const greenEnv = ref(0); 
		
		function fetchData() {
			return fetch(
				"http://devoxx2023.dev.k8s.signalocean.com/ServiceBusCount",{cache: 'no-cache'}
			)
				.then(async (res) => {
					console.log(res);
					var textValue = await res.json();
					if (textValue.environment == "blue") {
						blueEnv.value = textValue.queueMessagesProccessed;
					}
					if (textValue.environment == "green") {
						greenEnv.value = textValue.queueMessagesProccessed;
					}
				})
				.catch((err) => {
					throw err;
				});
		}

		onMounted(() => {
			setInterval(() => fetchData(), 10000);
		});

		return {
			blueEnv,
			greenEnv,
			response, 
		};
	},
};
</script>

<!-- Add "scoped" attribute to limit CSS to this component only -->
<style scoped>
.squares {
	display: flex;
	justify-content: center;
}

.squareGreen {
	text-align: center;
	display: inline-block;
	height: 150px;
	width: 150px;
	background-color: #008000;
	margin-right: 5%;
	border-style: ridge;
	display: flex;
	align-items: center;
	justify-content: center;
}

.squareBlue {
	text-align: center;
	display: inline-block;
	height: 150px;
	width: 150px;
	background-color: #0000FF;
	display: flex;
	border-style: ridge;
	display: flex;
	align-items: center;
	justify-content: center;

}
</style>
