using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace CachingProxy
{
    internal class Program
    {
        static ConcurrentDictionary<string, string> cache = new();
        static async Task Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--clear-cache")
            {
                cache.Clear();
                Console.WriteLine("Cache cleared.");
                return;
            }

            string origin = "";
            int port = 0;

            for (int i = 0; i < args.Length; i++)
            {
                bool isArgument = i + 1 < args.Length;
                if (args[i] == "--port" && isArgument) 
                {
                    int.TryParse(args[i + 1], out port);
                }
                if (args[i] == "--origin" && isArgument)
                {
                    origin = args[i + 1];

                }
            }

            if (port <= 0 || string.IsNullOrEmpty(origin)) 
            {
                Console.WriteLine("Usage: caching-proxy --port \"number\" --origin \"url\"");
                return;
            }

            HttpListener listener = new();
            listener.Prefixes.Add($"http://localhost:{port}/");
            listener.Start();
            Console.WriteLine($"Proxy server running on http://localhost:{port} forwarding to {origin}");

            while (true)
            {
                HttpListenerContext context = await listener.GetContextAsync();
                _ = Task.Run(() => HandleRequest(context, origin));
            }
        }

        static async Task HandleRequest(HttpListenerContext context, string origin)
        {
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;
            string pathAndQuery = request.RawUrl;

            if(cache.TryGetValue(pathAndQuery, out string cachedResponse))
            {
                response.AddHeader("X-Cache", "HIT");
                byte[] encodedResponse = Encoding.UTF8.GetBytes(cachedResponse);
                response.ContentType = "application/json";
                response.ContentLength64 = encodedResponse.Length;
                await response.OutputStream.WriteAsync(encodedResponse, 0, encodedResponse.Length);
                response.Close();
                return;
            }

            try
            {
                HttpClient httpClient = new HttpClient();
                HttpResponseMessage originResponse = await httpClient.GetAsync(origin + pathAndQuery);
                string responseBody = await originResponse.Content.ReadAsStringAsync();

                cache[pathAndQuery] = responseBody;
                response.AddHeader("X-Cache", "MISS");
                byte[] encodedResponse = Encoding.UTF8.GetBytes(responseBody);
                response.ContentType = "application/json";
                response.ContentLength64 = encodedResponse.Length;
                await response.OutputStream.WriteAsync(encodedResponse, 0, encodedResponse.Length);
            }
            catch (Exception ex) 
            {
                response.StatusCode = 500;
                byte[] encodedMessage = Encoding.UTF8.GetBytes($"Error: {ex.Message}");
                await response.OutputStream.WriteAsync(encodedMessage, 0, encodedMessage.Length);
            }
            finally
            {
                response.Close();
            }
        }
    }
}
