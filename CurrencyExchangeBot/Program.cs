using Newtonsoft.Json;
using CurrencyExchangeBot.Models;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types.Enums;
using System.Text;
using System.Collections.Generic;
using Newtonsoft.Json.Serialization;

namespace CurrencyExchangeBot
{
    public static class Program
    {
        private static TelegramBotClient Bot;
        private static HttpClient Client = new HttpClient();
        private static Dictionary<long, ApiResponse> LastResponse = new Dictionary<long, ApiResponse>();

        public static async Task Main()
        {
            Client.BaseAddress = new Uri(Configuration.BaseAddress);

            Bot = new TelegramBotClient(Configuration.BotToken);

            var me = await Bot.GetMeAsync();
            Console.Title = me.Username;

            Bot.OnMessage += BotOnMessageReceived;
            Bot.OnMessageEdited += BotOnMessageReceived;
            Bot.OnReceiveError += BotOnReceiveError;

            Bot.StartReceiving(Array.Empty<UpdateType>());
            Console.WriteLine($"Бот запущений для @{me.Username}");

            Console.ReadLine();
            Bot.StopReceiving();
        }

        private async static void SendText(long chatId, string text)
        {
            await Bot.SendTextMessageAsync(chatId, text);
        }

        private static void SendSuccess(long chatId)
        {
            SendText(chatId, "Успіх!");
        }

        private static void SendError(long chatId, string text)
        {
            SendText(chatId, $"Помилка: {text}!");
        }

        private static void SendInvalidFormatError(long chatId)
        {
            SendError(chatId, "невірний формат");
            Usage(chatId);
        }

        private static void SendAPIError(long chatId)
        {
            SendError(chatId, "помилка АПІ");
        }

        private static void BotOnMessageReceived(object sender, MessageEventArgs messageEventArgs)
        {
            var message = messageEventArgs.Message;
            if (message == null || message.Type != MessageType.Text)
            {
                SendInvalidFormatError(message.Chat.Id);
                return;
            }

            long chatId = message.Chat.Id;
            string[] parts = message.Text.Split(' ');

            switch (parts.First())
            {
                case "/names":
                    if (!ArgumentsCount(1))
                    {
                        SendInvalidFormatError(chatId);
                        return;
                    }
                    NamesAndCountries(chatId, parts[1].ToUpper());
                    break;

                case "/convert":
                    if (!ArgumentsCount(3))
                    {
                        SendInvalidFormatError(chatId);
                        return;
                    }
                    try
                    {
                        Convert(chatId, parts[1].ToUpper(), parts[2].ToUpper(), int.Parse(parts[3]));
                    }
                    catch
                    {
                        SendInvalidFormatError(chatId);
                    }
                    break;

                case "/savelast":
                    if (!ArgumentsCount(1))
                    {
                        SendInvalidFormatError(chatId);
                        return;
                    }
                    SaveLast(chatId, parts[1].ToUpper());
                    break;

                case "/getconverted":
                    if (!ArgumentsCount(1))
                    {
                        SendInvalidFormatError(chatId);
                        return;
                    }
                    GetConverted(chatId, parts[1].ToUpper());
                    break;

                case "/getall":
                    GetAll(chatId);
                    break;

                default:
                    Usage(chatId);
                    break;
            }

            bool ArgumentsCount(int amount)
            {
                if (parts.Length != amount + 1) return false;
                return true;
            }
        }

        private static void Usage(long chatId)
        {
            const string usage = "Користування ботом:\n" +
                                    "/names [startsWith] - отримати назви валют, які починаються з startsWith, та країни, в яких вони використовуються\n" +
                                    "/convert [from] [to] [amount] - конвертувати з валюти from в валюту to з кількістю amount\n" +
                                    "/savelast - зберегти останню конвертацію в бд\n" +
                                    "/getconverted [currency] - отримати з бд останню конвертацію із вказаним значенням currency\n" +
                                    "/getall - отримати всі конвертації з бд";
            SendText(chatId, usage);
        }


        private static void NamesAndCountries(long chatId, string startsWith)
        {
            HttpResponseMessage response;
            try
            {
                response = Client.GetAsync("NamesAndCountries").Result;
            }
            catch (Exception e)
            {
                SendError(chatId, e.Message);
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                SendAPIError(chatId);
                return;
            }

            var obj = JsonConvert.DeserializeObject<CurrenciesNames>(response.Content.ReadAsStringAsync().Result);
            StringBuilder sb = new StringBuilder();
            Type type = obj.GetType();

            int i = 1;
            foreach (var f in type.GetProperties().Where(f => f.CanRead && f.Name.ToUpper().StartsWith(startsWith)))
            {
                sb.AppendLine(string.Format("Символ: {0}, {1}", f.Name, f.GetValue(obj)));
                if (i % 20 == 0)
                {
                    SendText(chatId, sb.ToString());
                    sb.Clear();
                }
                i++;
            }
            string res = sb.ToString();
            SendText(chatId, string.IsNullOrEmpty(res) ? "Не знайдено!" : res);
        }

        private static void Convert(long chatId, string from, string to, int amount)
        {
            if (amount == 0)
            {
                SendError(chatId, "недопустиме значення, amount має бути більше 0");
                return;
            }

            HttpResponseMessage response;
            try
            {
                response = Client.GetAsync($"convert?from={from}&to={to}&amount={amount}").Result;
            }
            catch (Exception e)
            {
                SendError(chatId, e.Message);
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                SendAPIError(chatId);
                return;
            }

            var obj = JsonConvert.DeserializeObject<ApiResponse>(response.Content.ReadAsStringAsync().Result);
            if (double.Parse(obj.Value) == 0)
            {
                SendError(chatId, "валюта задана невірно");
                return;
            }

            if (LastResponse.ContainsKey(chatId)) LastResponse[chatId] = obj;
            else LastResponse.Add(chatId, obj);
            SendText(chatId, obj.ToString());
        }

        private static void SaveLast(long chatId, string currency)
        {
            if (!LastResponse.ContainsKey(chatId))
            {
                SendError(chatId, "ще не було успішних запитів");
                return;
            }

            HttpResponseMessage response;
            try
            {
                var serializerSettings = new JsonSerializerSettings();
                serializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
                response = Client.PostAsync($"addconverted?Currency={currency}", new StringContent(JsonConvert.SerializeObject(LastResponse[chatId], serializerSettings), Encoding.Unicode, "application/json")).Result;
            }
            catch (Exception e)
            {
                SendError(chatId, e.Message);
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                SendAPIError(chatId);
                return;
            }
            else
            {
                SendSuccess(chatId);
            }
        }

        private static void GetConverted(long chatId, string currency)
        {
            HttpResponseMessage response;
            try
            {
                response = Client.GetAsync($"getconverted?Currency={currency}").Result;
            }
            catch (Exception e)
            {
                SendError(chatId, e.Message);
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                SendAPIError(chatId);
                return;
            }

            var obj = JsonConvert.DeserializeObject<DbResponse>(response.Content.ReadAsStringAsync().Result);
            if (obj == null || double.Parse(obj.Value) == 0)
            {
                SendError(chatId, $"немає збережених записів для валюти \"{currency}\"");
                return;
            }

            SendText(chatId, obj.ToString());
        }

        private static void GetAll(long chatId)
        {
            HttpResponseMessage response;
            try
            {
                response = Client.GetAsync($"all").Result;
            }
            catch (Exception e)
            {
                SendError(chatId, e.Message);
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                SendAPIError(chatId);
                return;
            }

            var objs = JsonConvert.DeserializeObject<List<ApiResponse>>(response.Content.ReadAsStringAsync().Result);
            if (objs == null || objs.Count == 0)
            {
                SendError(chatId, $"немає збережених записів");
                return;
            }

            SendText(chatId, objs.Select(res => res.ToString()).Aggregate((i, j) => i + "\n" + j));
        }

        private static void BotOnReceiveError(object sender, ReceiveErrorEventArgs receiveErrorEventArgs)
        {
            Console.WriteLine("Помилка: {0} — {1}",
                receiveErrorEventArgs.ApiRequestException.ErrorCode,
                receiveErrorEventArgs.ApiRequestException.Message
            );
        }
    }
}
