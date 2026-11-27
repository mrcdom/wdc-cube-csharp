#:project ../Shopping.Client/Shopping.Client.csproj
#:property TargetFrameworks=net11.0
#:property PublishAot=false

// Checks the compose setup end to end, from outside: every instance ready, a sign-in answered by two instances, a
// client's remote transactions kept on one instance by nginx, a browser kept on one Blazor instance, nginx's limit on
// /api/auth, and every instance's metrics in Prometheus. Run it after `docker compose up -d --wait`:
//     dotnet run check.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shopping.Client;
using Shopping.Domain.Products;
using Shopping.Domain.Users;
using Wdc.Domain;

const string Api = "http://localhost:5080", Blazor = "http://localhost:5090", Prometheus = "http://localhost:9090";
string[] servers = ["http://localhost:5081", "http://localhost:5082"];
string[] blazors = ["http://localhost:5091", "http://localhost:5092"];
var failures = new List<string>();
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

void Check(string what, bool ok)
{
    Console.WriteLine((ok ? "ok   " : "FAIL ") + what);
    if (!ok)
    {
        failures.Add(what);
    }
}

async Task<bool> WaitFor(Func<Task<bool>> condition, int seconds)
{
    var until = DateTime.UtcNow.AddSeconds(seconds);
    while (DateTime.UtcNow < until)
    {
        try
        {
            if (await condition())
            {
                return true;
            }
        }
        catch (HttpRequestException)
        {
        }

        await Task.Delay(2000);
    }

    return false;
}

async Task<bool> Ready(string address) =>
    (await http.GetAsync(address + "/health/ready")).StatusCode == HttpStatusCode.OK;

// nginx limits /api/auth per address, one request a second past its burst. A sign-in takes two (the challenge and the
// login): waiting one second would hand the new slot to the challenge and refuse the login again, every time.
async Task<Shopping.Presentation.Infra.Subject?> SignIn(ShoppingClient client)
{
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            return await client.Authentication.Login("fulano", "fulano");
        }
        catch (TooManyRequestsException) when (attempt < 30)
        {
            await Task.Delay(3000);
        }
    }
}

async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

foreach (var address in servers.Concat(blazors).Append(Api).Append(Blazor))
{
    Check($"{address} is ready", await WaitFor(() => Ready(address), 120));
}

// The challenge from one instance, the login on the other: the nonce is in the database.
var challenge = await Json(await http.GetAsync(servers[0] + "/api/auth/challenge?userName=admin"));
var nonce = challenge.GetProperty("nonce").GetString()!;
var derived = PasswordHash.Derive("admin", Convert.FromBase64String(challenge.GetProperty("salt").GetString()!),
    challenge.GetProperty("iterations").GetInt32());
using var login = await http.PostAsJsonAsync(servers[1] + "/api/auth/login",
    new Dictionary<string, string> { ["userName"] = "admin", ["nonce"] = nonce, ["digest"] = PasswordHash.Digest(derived, "admin", nonce) });
Check("a challenge from A answered on B", login.StatusCode == HttpStatusCode.OK);
if (login.StatusCode == HttpStatusCode.OK)
{
    var session = await Json(login);
    foreach (var server in servers)
    {
        using var count = new HttpRequestMessage(HttpMethod.Post, server + "/api/repo/product/count")
        {
            Content = JsonContent.Create(new Dictionary<string, object>()),
        };
        count.Headers.Authorization = new("Bearer", session.GetProperty("accessToken").GetString());
        Check($"the token works on {server}", (await http.SendAsync(count)).StatusCode == HttpStatusCode.OK);
    }

    using var refresh = await http.PostAsJsonAsync(servers[0] + "/api/auth/refresh",
        new Dictionary<string, string> { ["refreshToken"] = session.GetProperty("refreshToken").GetString()! });
    Check("the refresh token from B spent on A", refresh.StatusCode == HttpStatusCode.OK);
}

// Clients through nginx: each one's calls stay on one instance, and the clients spread over both.
var spread = new HashSet<string>();
for (var i = 0; i < 12; i++)
{
    var seen = new UpstreamRecorder();
    var client = new ShoppingClient(new HttpClient(seen) { BaseAddress = new Uri(Api) });
    var subject = await SignIn(client);
    var counts = 0;
    for (var t = 0; t < 3; t++)
    {
        counts += await client.Repositories.Transactions.Required(async _ =>
            await client.Repositories.Products.Count(new ProductCriteria())
            + await client.Repositories.Products.Count(new ProductCriteria().Price.Gt(10m)));
    }

    Check($"client {i}: signed in, 3 remote transactions, every call on {string.Join(", ", seen.WithClientId)}",
        subject is not null && counts > 0 && seen.WithClientId.Count == 1);
    spread.UnionWith(seen.WithClientId);
}

Check($"the clients spread over both API instances ({spread.Count})", spread.Count == 2);

// A browser on Blazor: the first answer sets the cookie that keeps the page and its circuit on one instance.
var browsers = new HashSet<string>();
for (var i = 0; i < 12; i++)
{
    var cookies = new CookieContainer();
    using var browser = new HttpClient(new HttpClientHandler { CookieContainer = cookies }) { BaseAddress = new Uri(Blazor) };
    var answered = new List<string>();
    using (var page = await browser.GetAsync("/"))
    {
        answered.Add(page.Headers.GetValues("X-Upstream").First());
    }

    for (var n = 0; n < 3; n++)
    {
        using var negotiate = await browser.PostAsync("/_blazor/negotiate?negotiateVersion=1", null);
        answered.Add(negotiate.Headers.GetValues("X-Upstream").First());
    }

    Check($"browser {i}: the page and its circuit's calls on one Blazor instance", answered.Distinct().Count() == 1);
    browsers.Add(answered[0]);
}

Check($"the browsers spread over both Blazor instances ({browsers.Count})", browsers.Count == 2);

// nginx's limit per address on /api/auth.
var statuses = new List<HttpStatusCode>();
for (var i = 0; i < 100; i++)
{
    using var answer = await http.GetAsync(Api + "/api/auth/challenge?userName=nobody");
    statuses.Add(answer.StatusCode);
}

Check($"nginx limits /api/auth per address ({statuses.Count(s => s == HttpStatusCode.TooManyRequests)} of 100 answered 429)",
    statuses.Contains(HttpStatusCode.TooManyRequests));

// The metrics every instance pushed (the live Blazor applications appear once a browser opens a circuit).
async Task<int> Series(string query)
{
    var answer = await Json(await http.GetAsync(Prometheus + "/api/v1/query?query=" + Uri.EscapeDataString(query)));
    return answer.GetProperty("data").GetProperty("result").GetArrayLength();
}

Check("Prometheus has metrics from the four instances",
    await WaitFor(async () => await Series("count by (instance) ({__name__=~\"http_server_request_duration_seconds.*\"})") == 4, 60));
Check("and the remote transactions of both API instances",
    await WaitFor(async () => await Series("count by (instance) ({__name__=~\"wdc_remote_transactions_total\"})") == 2, 60));
Check("and the sign-ins",
    await WaitFor(async () => await Series("shopping_signins_total{outcome=\"ok\"}") > 0, 60));

Console.WriteLine(failures.Count == 0 ? "all checks passed" : $"{failures.Count} check(s) failed");
return failures.Count == 0 ? 0 : 1;

/// <summary>Which instance answered each call that carried a client id.</summary>
sealed class UpstreamRecorder() : DelegatingHandler(new HttpClientHandler())
{
    public HashSet<string> WithClientId { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (request.Headers.Contains("X-Client-Id") && response.Headers.TryGetValues("X-Upstream", out var upstream))
        {
            lock (WithClientId)
            {
                WithClientId.Add(upstream.First());
            }
        }

        return response;
    }
}
