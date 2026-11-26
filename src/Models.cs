using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WorldAI
{
    internal static class Models
    {
        private const string ChatPath = "/chat/completions";
        private static readonly HttpClient Client = new HttpClient(new HttpClientHandler {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            UseCookies = false
        }) { Timeout = Timeout.InfiniteTimeSpan };

        internal static Uri Endpoint(string address)
        {
            Uri uri;
            if (!Uri.TryCreate((address ?? "").Trim(), UriKind.Absolute, out uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https") || !String.IsNullOrEmpty(uri.UserInfo) ||
                !String.IsNullOrEmpty(uri.Fragment))
                throw new InvalidOperationException("请输入有效的 HTTP 或 HTTPS API 地址。");

            var result = new UriBuilder(uri);
            string path = result.Path.TrimEnd('/');
            result.Path = path.EndsWith(ChatPath, StringComparison.OrdinalIgnoreCase)
                ? path : (path.Length == 0 ? "/v1" : path) + ChatPath;
            return result.Uri;
        }

        internal static async Task<string[]> ListIds(string address, string encryptedKey, CancellationToken token)
        {
            var url = new UriBuilder(Endpoint(address));
            url.Path = url.Path.Substring(0, url.Path.Length - ChatPath.Length) + "/models";
            using (var request = CreateRequest(HttpMethod.Get, url.Uri, encryptedKey))
            {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                using (var response = await Client.SendAsync(request, token))
                {
                    await EnsureSuccess(response);
                    string body = await response.Content.ReadAsStringAsync();
                    object[] data;
                    try
                    {
                        data = Cards.Get(Store.Json().DeserializeObject(body), "data") as object[];
                    }
                    catch (ArgumentException)
                    {
                        data = null;
                    }
                    if (data == null)
                        throw new InvalidOperationException("服务没有返回可识别的模型列表。");

                    return data.Select(item => Cards.Get(item, "id") as string)
                        .Where(id => !String.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal)
                        .OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray();
                }
            }
        }

        internal static async Task<double[][]> Embed(Model api, string model, string[] input, CancellationToken token)
        {
            var address = new UriBuilder(Endpoint(api.Endpoint));
            address.Path = address.Path.Substring(0, address.Path.Length - ChatPath.Length) + "/embeddings";
            using (var request = CreateRequest(HttpMethod.Post, address.Uri, api.EncryptedKey))
            {
                var payload = new { model = model, input = input };
                request.Content = new StringContent(Store.Json().Serialize(payload), Encoding.UTF8, "application/json");
                using (var response = await Client.SendAsync(request, token))
                {
                    await EnsureSuccess(response);
                    var data = Cards.Get(Store.Json().DeserializeObject(await response.Content.ReadAsStringAsync()), "data") as object[];
                    if (data == null || data.Length != input.Length) throw new InvalidDataException("向量接口返回了不完整的数据。");
                    var vectors = new double[input.Length][];
                    foreach (var row in data)
                    {
                        var raw = Cards.Get(row, "embedding") as object[];
                        var index = Cards.Get(row, "index");
                        if (!(index is int) || (int)index < 0 || (int)index >= vectors.Length ||
                            vectors[(int)index] != null || raw == null || raw.Length < 2 || raw.Length > 8192 ||
                            raw.Any(value => !(value is int || value is long || value is decimal || value is double)))
                            throw new InvalidDataException("向量接口的响应格式不兼容。");
                        vectors[(int)index] = raw.Select(value => Convert.ToDouble(value)).ToArray();
                    }
                    return vectors;
                }
            }
        }

        internal static async Task Generate(Model model, List<Message> messages,
            Action<string> receive, CancellationToken token, bool summarize = false, bool stream = true)
        {
            if (model.Demo)
            {
                string last = messages.Last().Text ?? "";
                string reply = summarize
                    ? "玩家在雾港遇见信使林渡，收下了一封受潮的信。信件与今晚突然亮起的旧灯塔有关。"
                    : last.IndexOf("灯塔", StringComparison.Ordinal) >= 0
                        ? "林渡望向雾里。“三次。旧航线的人在求救。”他摸出一把锈钥匙，放在栏杆上。“去灯塔的门还认这把钥匙。你要带上它吗？”"
                        : "林渡侧头听了听，码头尽头传来一声空响。“你可以先拆信，”他说，“或者跟我去看看是谁把渡船推回来了。”";
                if (!stream)
                {
                    await Task.Delay(500, token);
                    receive(reply);
                    return;
                }
                for (int i = 0; i < reply.Length; i += 3)
                {
                    await Task.Delay(28, token);
                    receive(reply.Substring(i, Math.Min(3, reply.Length - i)));
                }
                return;
            }

            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromMinutes(4));
                try
                {
                    await Request(model, messages, receive, timeout.Token, stream);
                }
                catch (Exception)
                {
                    if (token.IsCancellationRequested) throw new OperationCanceledException(token);
                    if (timeout.IsCancellationRequested)
                        throw new TimeoutException("模型请求超过 4 分钟，可以检查连接后重试。");
                    throw;
                }
            }
        }

        private static async Task Request(Model model, List<Message> messages, Action<string> receive, CancellationToken token, bool stream)
        {
            using (var request = CreateRequest(HttpMethod.Post, Endpoint(model.Endpoint), model.EncryptedKey))
            {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(stream ? "text/event-stream" : "application/json"));
                var body = new {
                    model = model.ModelName, stream = stream,
                    messages = messages.Select(message => new { role = message.Role, content = message.Text ?? "" }).ToArray()
                };
                request.Content = new StringContent(Store.Json().Serialize(body), Encoding.UTF8, "application/json");

                using (var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token))
                using (token.Register(response.Dispose))
                {
                    await EnsureSuccess(response);

                    using (var reader = new StreamReader(await response.Content.ReadAsStreamAsync(), Encoding.UTF8))
                    {
                        var payload = new StringBuilder();
                        string line;
                        bool sawEvent = false;
                        while ((line = await reader.ReadLineAsync()) != null)
                        {
                            token.ThrowIfCancellationRequested();
                            if (line.StartsWith("data:", StringComparison.Ordinal))
                            {
                                sawEvent = true;
                                string data = line.Substring(5).Trim();
                                if (data == "[DONE]") return;
                                if (data.Length > 0) Consume(data, receive);
                            }
                            else if (!sawEvent && !line.StartsWith(":", StringComparison.Ordinal))
                            {
                                payload.AppendLine(line);
                            }
                        }
                        token.ThrowIfCancellationRequested();
                        if (!sawEvent && payload.ToString().Trim().Length > 0)
                            Consume(payload.ToString(), receive);
                    }
                }
            }
        }

        private static HttpRequestMessage CreateRequest(HttpMethod method, Uri url, string encryptedKey)
        {
            string key = Store.Decrypt(encryptedKey);
            var authorization = key.Length == 0 ? null : new AuthenticationHeaderValue("Bearer", key);
            var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = authorization;
            return request;
        }

        private static async Task EnsureSuccess(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode) return;
            string reason = ErrorBody(await response.Content.ReadAsStringAsync());
            throw new InvalidOperationException("API 返回 HTTP " + (int)response.StatusCode +
                " " + response.ReasonPhrase + (reason.Length > 0 ? "\n" + reason : ""));
        }

        private static string ErrorBody(string json)
        {
            try
            {
                var error = Cards.Get(Store.Json().DeserializeObject(json), "error");
                var message = Cards.Get(error, "message") as string ?? error as string;
                if (message != null) return message.Length > 400 ? message.Substring(0, 400) + "…" : message;
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            return "";
        }

        private static void Consume(string json, Action<string> receive)
        {
            var value = Store.Json().DeserializeObject(json);
            if (Cards.Get(value, "error") != null)
                throw new InvalidOperationException("模型服务返回错误。\n" + ErrorBody(json));
            var choices = Cards.Get(value, "choices") as object[];
            if (choices == null || choices.Length == 0) return;
            var message = Cards.Get(choices[0], "delta") ?? Cards.Get(choices[0], "message");
            var content = Cards.Get(message, "content") as string;
            if (!String.IsNullOrEmpty(content)) receive(content);
        }
    }
}
