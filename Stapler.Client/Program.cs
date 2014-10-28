using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;

namespace Stapler.Client
{
    class Program
    {
        private static string _path;
        private static string _args;
        private const string UrlFormat = "http://localhost:13711/{0}/";
        private const string UnityExecutable = @"C:\Program Files (x86)\Unity\Editor\Unity.exe";

        public static string Base64Encode(string plainText)
        {
            var plainTextBytes = Encoding.UTF8.GetBytes(plainText);
            return Convert.ToBase64String(plainTextBytes);
        }

        static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("exe project method");
                return;
            }
            _path = args[0];
            _args = args[1];
            var t = new Task(SendOrLaunchUnity);
            t.Start();
            t.Wait();
            Console.ReadLine();
        }

        static async void SendOrLaunchUnity()
        {
            var unityStylePath = _path.Replace("\\", "/").TrimEnd('/');
            var encodedPath = Base64Encode(unityStylePath);
            var url = string.Format(UrlFormat, encodedPath);

            using (var client = new HttpClient())
            {
                try
                {
                    using (var response = await client.PostAsync(url, new StringContent(_args)))
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            await HandleResponse(response);
                        }
                        else
                        {
                            Console.WriteLine("Got error. Invoking Unity: {0}", response.StatusCode);
                            InvokeUnity();
                        }
                    }
                }
                catch (Exception)
                {
                    Console.WriteLine("No response. Invoking Unity: {0}");
                    InvokeUnity();
                }

            }
        }

        private static async Task HandleResponse(HttpResponseMessage response)
        {
            using (var content = response.Content)
            {
                string result = await content.ReadAsStringAsync();
                if (result != null)
                {
                    Console.WriteLine(result);
                    // TODO: log messages?
                }
            }
        }

        private static void InvokeUnity()
        {
            Process.Start(UnityExecutable, string.Format("-batchMode -executeMethod {0}", _args)); // TODO: args
        }
    }


}
