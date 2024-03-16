using MQTTnet.Client;
using MQTTnet;
using System.Text.Json;
using System.Runtime.InteropServices;

namespace water_mqtt 
{
    internal class Program 
    {
        static Dictionary<string, float> midnightReadings = new Dictionary<string, float>();
        static Dictionary<string, double> costReadings = new Dictionary<string, double>();
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
            if (today != DateTime.Today) {
                midnightReadings.Clear(); 
                costReadings.Clear();
            }

            if (!midnightReadings.ContainsKey(name)) {
                midnightReadings[name] = totalM3; // Store initial reading
                costReadings[name] = 0;
            }

            float dailyUse = totalM3 - midnightReadings[name]; // Calculate use and cost even if it is the initial reading this day to reset info in HA
            SendDailyUsage(name, dailyUse);
        }
        static void SendDailyUsage(string name, float dailyUse)
        {
            string name_total_cost = "";
            double total_ZL = 0;
            var usageData = new { 
                media = "water",
                name = name,
                dailyUse = dailyUse
            };
            CalculateCost(name, dailyUse, false);
            if (name=="Cold-water-bathroom" || name=="Hot-water-bathroom") 
            {
                name_total_cost = "total_bathroom_water_cost";
                total_ZL = CalculateCost("-bathroom", 0, true);
            } else {
                name_total_cost = "total_kitchen_water_cost";
                total_ZL = CalculateCost("-kitchen", 0, true);
            }
            var totalCostsData = new { 
                media = "money",
                name = name_total_cost,
                total_ZL = total_ZL
            };
            string usageDataJson = JsonSerializer.Serialize(usageData);
            string totalCostDataJason = JsonSerializer.Serialize(totalCostsData);
        }

        static double CalculateCost(string name, float dailyUse, bool total)
        {
            double cost = 0;
            if (total) 
            {
                if (name=="Cold-water-bathroom" || name=="Hot-water-bathroom")
                    cost = costReadings.Where(kvp => kvp.Key.EndsWith("-bathroom")).Sum(kvp => kvp.Value);
                else if (name=="Cold-water-kitchen" || name=="Hot-water-kitchen")
                    cost = costReadings.Where(kvp => kvp.Key.EndsWith("-kitchen")).Sum(kvp => kvp.Value);
            } else {
                switch (name) {
                    case "Cold-water-bathroom": case "Cold-water-kitchem":
                        cost = Math.Round(dailyUse * (prices["cold_water"]/1000) + dailyUse * (prices["sewage"]/1000),2);
                        break;
                    case "Hot-water-bathroon": case "Hot-water-kitchen":
                        cost = Math.Round(dailyUse * (prices["hot_water"]/1000) + dailyUse * (prices["sewage"]/1000),2);
                        break;
                }
                costReadings[name] = cost; // Saves the cost to dictionary for futher calculations
            }
            return cost;
        }
        static async Task Main()
        {
            await Connect_Client();
        }
    }
}