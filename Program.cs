using MQTTnet.Client;
using MQTTnet;
using System.Text.Json;

namespace water_mqtt 
{
    internal class Program 
    {
        static Dictionary<string, float> midnightReadings = new Dictionary<string, float>();
        static Dictionary<string, float> prices = new Dictionary<string, float>()
        {
            { "cold_water", 5.05f },
            { "hot_water", 63.05f },
            { "sewage", 7.48f }
        };
        static async Task Connect_Client()
        {
            var mqttFactory = new MqttFactory();
            using (var mqttClient = mqttFactory.CreateMqttClient())
            {
                int port = 1883;
                string clientId = Guid.NewGuid().ToString();
                string broker = "ip_addr"; 
                string username = "usr_name";
                string password = "usr_passwd";
                var factory = new MqttFactory();

                var options = new MqttClientOptionsBuilder()
                    .WithTcpServer(broker, port) // MQTT broker address and port
                    .WithCredentials(username, password) // Set username and password
                    .WithClientId(clientId)
                    .WithCleanSession()
                    .Build();

                try
                {
                    var response = await mqttClient.ConnectAsync(options, CancellationToken.None);
                    if (response.ResultCode == MqttClientConnectResultCode.Success)
                    {
                        mqttClient.ApplicationMessageReceivedAsync += async e =>
                        {
                            await ProcessMessage(e.ApplicationMessage.PayloadSegment);
                        }; 

                        var subscribeOptionsBuilder = mqttFactory.CreateSubscribeOptionsBuilder();
                        subscribeOptionsBuilder.WithTopicFilter(f => f.WithTopic("Cold-water-bathroom"));
                        subscribeOptionsBuilder.WithTopicFilter(f => f.WithTopic("Cold-water-kitchen"));
                        subscribeOptionsBuilder.WithTopicFilter(f => f.WithTopic("Hot-water-bathroom"));
                        subscribeOptionsBuilder.WithTopicFilter(f => f.WithTopic("Hot-water-kitchen"));
                        subscribeOptionsBuilder.WithTopicFilter(f => f.WithTopic("test-water"));

                        await mqttClient.SubscribeAsync(subscribeOptionsBuilder.Build(), CancellationToken.None);

                        Console.WriteLine("MQTT client subscribed to topic, press any kay to exit.");
                        Console.Read();
                    }
                }
                catch (OperationCanceledException ex)
                {
                    Console.WriteLine($"MQTT connection failed: {ex.Message}");
                }
            }
        }
        static async Task ProcessMessage(ArraySegment<byte> payloadSegment) 
        {
            string jsonPayload = System.Text.Encoding.UTF8.GetString(payloadSegment);
            try
            {
                var data = JsonSerializer.Deserialize<dynamic>(jsonPayload);

                string name = data.GetProperty("name").GetString();
                float totalM3 = data.GetProperty("total_m3").GetSingle()*1000;

                CalculateDailyUsage(name, totalM3);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing message: {ex.Message}");
            }
        }
        static void CalculateDailyUsage(string name, float totalM3)
        {
            DateTime today = DateTime.Today;

            if (!midnightReadings.ContainsKey(name)) {
                midnightReadings[name] = totalM3; // Store initial reading
            } else {
                float dailyUse = totalM3 - midnightReadings[name]; 
                SendDailyUsage(name, dailyUse);
            }
            if (today != DateTime.Today) {
                midnightReadings.Clear(); 
            }
        }
        static void SendDailyUsage(string name, float dailyUse)
        {
            var usageData = new { 
                name = name,
                dailyUse = dailyUse,
                cost = CalculateCost(name, dailyUse)
            };

            string jsonData = JsonSerializer.Serialize(usageData);

            Console.WriteLine(jsonData);
        }

        static double CalculateCost(string name, float dailyUse)
        {
            double cost = 0;
            switch (name) {
                case "Cold-water-bathroom":
                case "Cold-water-kitchem":
                    cost = Math.Round(dailyUse * (prices["cold_water"]/1000) + dailyUse * (prices["sewage"]/1000),2);
                    break;
                case "Hot-water-bathroom":
                case "Hot-water-kitchem":
                    cost = Math.Round(dailyUse * (prices["hot_water"]/1000) + dailyUse * (prices["sewage"]/1000),2);
                    break;
            }
            return cost;
        }
        static async Task Main()
        {
            await Connect_Client();
        }
    }
}