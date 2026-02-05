using Azure.Core;
using Azure.Identity;
using Azure.Storage.Files.Shares;

using McpSamples.OnedriveDownload.HybridApp.Configurations;
using McpSamples.OnedriveDownload.HybridApp.Services;
using McpSamples.Shared.Configurations;
using McpSamples.Shared.Extensions;

using Microsoft.Graph;

using Constants = McpSamples.OnedriveDownload.HybridApp.Constants;
using WebApplication = Microsoft.AspNetCore.Builder.WebApplication;

var envs = Environment.GetEnvironmentVariables();
var useStreamableHttp = AppSettings.UseStreamableHttp(envs, args);

// // Force HTTP mode unless --stdio is explicitly passed
// if (!args.Contains("--stdio", StringComparer.InvariantCultureIgnoreCase))
// {
//     useStreamableHttp = true;
// }

IHostApplicationBuilder builder = useStreamableHttp
                                ? WebApplication.CreateBuilder(args)
                                : Host.CreateApplicationBuilder(args);

if (useStreamableHttp == true)
{
    var port = Environment.GetEnvironmentVariable(Constants.AzureFunctionsCustomHandlerPortEnvironmentKey) ?? $"{Constants.DefaultAppPort}";
    (builder as WebApplicationBuilder)!.WebHost.UseUrls(string.Format(Constants.DefaultAppUrl, port));

    Console.WriteLine($"Listening on port {port}");
}

builder.Services.AddHttpContextAccessor();
builder.Services.AddAppSettings<OnedriveDownloadAppSettings>(builder.Configuration, args);

// ★ HttpClient 등록 (토큰 교환 프록시용)
// builder.Services.AddHttpClient();

// ★ Token Passthrough: 클라이언트(VSCode)가 보낸 Authorization 헤더에서 토큰을 꺼내 사용
// VSCode에서 Microsoft 인증을 하면 팝업이 뜨고, 토큰을 요청 헤더에 포함시켜 보냄
builder.Services.AddScoped<GraphServiceClient>(sp =>
{
    // var httpContextAccessor = sp.GetRequiredService<IHttpContextAccessor>();
    // var httpContext = httpContextAccessor.HttpContext;

    // // ★★★ [핵심] /authorize 요청이면 토큰 검사 건너뛰기 (리다이렉트 될 것이므로)
    // if (httpContext != null && httpContext.Request.Path.Value!.StartsWith("/authorize", StringComparison.OrdinalIgnoreCase))
    // {
    //     return new GraphServiceClient(new AnonymousTokenCredential());
    // }

    // // 요청 헤더에서 Authorization: Bearer <token> 형태로 토큰을 꺼냄
    // string? authHeader = httpContext?.Request.Headers.Authorization.ToString();
    // string? accessToken = null;

    // if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    // {
    //     accessToken = authHeader.Substring("Bearer ".Length).Trim();
    // }

    // // 토큰이 없으면 Anonymous 반환 (미들웨어에서 검증)
    // if (string.IsNullOrEmpty(accessToken))
    // {
    //     return new GraphServiceClient(new AnonymousTokenCredential());
    // }

    // Console.WriteLine("[인증 성공] 클라이언트가 토큰을 보냈습니다!");

    // // 헤더에서 꺼낸 토큰을 사용하여 GraphServiceClient 생성
    // TokenCredential credential = new BearerTokenCredential(accessToken);
    // string[] scopes = [ Constants.DefaultScope ];
    // var client = new GraphServiceClient(credential, scopes);

    // return client;

    var settings = sp.GetRequiredService<OnedriveDownloadAppSettings>();
    var entraId = settings.EntraId;

    TokenCredential credential = entraId.UseManagedIdentity
                                   ? new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(entraId.UserAssignedClientId))
                                   : new ClientSecretCredential(entraId.TenantId, entraId.ClientId, entraId.ClientSecret);

    string[] scopes = [ Constants.DefaultScope ];
    var client = new GraphServiceClient(credential, scopes);

    return client;
});

builder.Services.AddScoped<ShareClient>(sp =>
{
    var settings = sp.GetRequiredService<OnedriveDownloadAppSettings>();
    var connectionString = settings.AzureStorage.ConnectionString;
    var shareName = settings.AzureStorage.FileShareName;

    var client = new ShareClient(connectionString, shareName);

    return client;
});

// Add Application Insights for proper Azure logging
// if (useStreamableHttp == true)
// {
//     builder.Services.AddApplicationInsightsTelemetry();
//     builder.Logging.AddApplicationInsights();
// }

// if (useStreamableHttp == true)
// {
//     var port = Environment.GetEnvironmentVariable(Constants.AzureFunctionsCustomHandlerPortEnvironmentKey) ?? $"{Constants.DefaultAppPort}";
//     (builder as Microsoft.AspNetCore.Builder.WebApplicationBuilder)!.WebHost.UseUrls(string.Format(Constants.DefaultAppUrl, port));

//     Console.WriteLine($"[INFO] Listening on port {port}");
//     builder.Services.AddHttpContextAccessor();
// }

// // Add Azure Key Vault configuration
// var keyVaultName = builder.Configuration["KeyVaultName"];
// if (!string.IsNullOrEmpty(keyVaultName))
// {
//     try
//     {
//         builder.Configuration.AddAzureKeyVault(
//             new Uri($"https://{keyVaultName}.vault.azure.net/"),
//             new DefaultAzureCredential());
//     }
//     catch (Exception ex)
//     {
//         // Log but don't fail - Key Vault might not be available in all environments
//         Console.WriteLine($"[WARNING] Failed to load Key Vault: {ex.Message}");
//     }
// }

// // Add authentication service
// builder.Services.AddScoped<IUserAuthenticationService, UserAuthenticationService>();

// // Add Azure File Share Sync Service
// builder.Services.AddSingleton<AzureFileShareSyncService>();

// if (useStreamableHttp == true)
// {
//     builder.Services.AddOpenApi("swagger", o =>
//     {
//         o.OpenApiVersion = Microsoft.OpenApi.OpenApiSpecVersion.OpenApi2_0;
//         o.AddDocumentTransformer<McpDocumentTransformer<OnedriveDownloadAppSettings>>();
//     });
//     builder.Services.AddOpenApi("openapi", o =>
//     {
//         o.OpenApiVersion = Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_0;
//         o.AddDocumentTransformer<McpDocumentTransformer<OnedriveDownloadAppSettings>>();
//     });
// }

IHost app = builder.BuildApp(useStreamableHttp);

// if (useStreamableHttp == true)
// {
//     var webApp = (app as Microsoft.AspNetCore.Builder.WebApplication)!;

//     // =========================================================================
//     // 1. 미들웨어 설정 (인증, CORS, 로깅)
//     // =========================================================================
//     webApp.Use(async (context, next) =>
//     {
//         var path = context.Request.Path.Value!;
//         var method = context.Request.Method;

//         // 1. CORS 헤더 강제 주입 (필수)
//         context.Response.Headers.Append("Access-Control-Allow-Origin", "*");
//         context.Response.Headers.Append("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
//         context.Response.Headers.Append("Access-Control-Allow-Headers", "Content-Type, Authorization");

//         // 2. OPTIONS (노크) 요청은 무조건 통과
//         if (method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
//         {
//             context.Response.StatusCode = 200;
//             return;
//         }

//         // 3. ★★★ [핵심 수정] 토큰 검사 면제 목록에 '/download' 추가 ★★★
//         // 이 줄이 없어서 브라우저가 다운로드하러 들어갔다가 쫓겨난(401/404) 겁니다.
//         if (path.StartsWith("/authorize", StringComparison.OrdinalIgnoreCase) ||
//             path.StartsWith("/token", StringComparison.OrdinalIgnoreCase) ||
//             path.StartsWith("/download", StringComparison.OrdinalIgnoreCase) || // <--- ★ 여기 추가됨!
//             path.StartsWith("/list-files", StringComparison.OrdinalIgnoreCase) || // <--- ★ 디버깅용도 추가
//             (path == "/" && context.Request.Query.ContainsKey("code")) ||
//             path.StartsWith("/.well-known", StringComparison.OrdinalIgnoreCase) ||
//             path == "/")
//         {
//             await next();
//             return;
//         }

//         // 4. 나머지 API 요청(MCP 등)은 토큰 검사
//         string? authHeader = context.Request.Headers.Authorization.ToString();
//         if (string.IsNullOrEmpty(authHeader))
//         {
//             Console.WriteLine($"❌ [토큰 없음] {method} {path}");
//             context.Response.Headers.Append("WWW-Authenticate", "Bearer realm=\"mcp\"");
//             context.Response.StatusCode = 401;
//             await context.Response.WriteAsync("Unauthorized: Access Token is required.");
//             return;
//         }

//         Console.WriteLine($"🔑 [토큰 수신] {authHeader.Substring(0, Math.Min(authHeader.Length, 15))}...");
//         await next();
//     });

//     // =========================================================================
//     // 2. [핵심] 토큰 교환 대행 (Proxy) 엔드포인트 (/token)
//     // VS Code가 서버로 잘못 보낸 토큰 요청을 MS로 대신 전달해 줍니다.
//     // =========================================================================
//     webApp.MapPost("/token", async (HttpContext context, IHttpClientFactory httpClientFactory) =>
//     {
//         Console.WriteLine("🔄 [Proxy] VS Code가 보낸 토큰 요청을 MS로 전달합니다...");
//         try
//         {
//             // 폼 데이터 읽기
//             var form = await context.Request.ReadFormAsync();
//             var formDict = form.ToDictionary(x => x.Key, x => x.Value.ToString());

//             // Microsoft로 요청 전달
//             var client = httpClientFactory.CreateClient();
//             var msTokenUrl = "https://login.microsoftonline.com/common/oauth2/v2.0/token";
//             var requestContent = new FormUrlEncodedContent(formDict);

//             var response = await client.PostAsync(msTokenUrl, requestContent);
//             var responseString = await response.Content.ReadAsStringAsync();

//             // 결과 반환
//             context.Response.StatusCode = (int)response.StatusCode;
//             context.Response.ContentType = "application/json";
//             await context.Response.WriteAsync(responseString);
//             Console.WriteLine($"✅ [Proxy] 토큰 교환 완료! 상태: {response.StatusCode}");
//         }
//         catch (Exception ex)
//         {
//             Console.WriteLine($"💥 [Proxy 실패] {ex.Message}");
//             context.Response.StatusCode = 500;
//             await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = ex.Message }));
//         }
//     });

//     // =========================================================================
//     // 3. [복구됨] 로그인 리다이렉트 엔드포인트 (/authorize)
//     // 아까 이 부분이 지워져서 404가 떴던 겁니다.
//     // =========================================================================
//     webApp.MapGet("/authorize", (HttpContext context) =>
//     {
//         var queryString = context.Request.QueryString.ToString();

//         // 안전장치: scope 강제 주입
//         if (!queryString.Contains("scope=", StringComparison.OrdinalIgnoreCase) &&
//             !queryString.Contains("scope%3D", StringComparison.OrdinalIgnoreCase))
//         {
//             queryString += string.IsNullOrEmpty(queryString) ? "?" : "&";
//             queryString += "scope=https%3A%2F%2Fgraph.microsoft.com%2F.default";
//         }

//         // MS 로그인 페이지로 리다이렉트 (VS Code가 보낸 포트 정보 유지)
//         var authUrl = $"https://login.microsoftonline.com/common/oauth2/v2.0/authorize{queryString}";

//         Console.WriteLine($"🔐 [인증] 리다이렉트: {authUrl}");
//         context.Response.Redirect(authUrl);
//         return Task.CompletedTask;
//     });

//     // =========================================================================
//     // 4. 인증 성공 화면 (/)
//     // =========================================================================
//     webApp.MapGet("/", async (HttpContext context) =>
//     {
//         context.Response.ContentType = "text/html; charset=utf-8";
//         await context.Response.WriteAsync(@"
//             <html><body>
//             <h1>✓ 인증 성공!</h1>
//             <p>VS Code로 돌아가세요.</p>
//             <script>setTimeout(function(){ window.location.href='vscode://'; }, 1000);</script>
//             </body></html>");
//     });

//     // =========================================================================
//     // 5. 다운로드 리다이렉트 핸들러 (/download)
//     // =========================================================================
//     webApp.MapGet("/download", async (HttpContext context) =>
//     {
//         var fileName = context.Request.Query["file"].ToString();
//         if (string.IsNullOrEmpty(fileName))
//         {
//             context.Response.StatusCode = 400;
//             await context.Response.WriteAsync("Error: 'file' parameter is missing.");
//             return;
//         }

//         try
//         {
//             var config = context.RequestServices.GetRequiredService<IConfiguration>();
//             var connectionString = config["AZURE_STORAGE_CONNECTION_STRING"];

//             if (string.IsNullOrEmpty(connectionString))
//             {
//                 await context.Response.WriteAsync("Error: Storage connection string not configured.");
//                 return;
//             }

//             var shareClient = new ShareClient(connectionString, "downloads");
//             var fileClient = shareClient.GetRootDirectoryClient().GetFileClient(fileName);

//             // 1. 파일이 진짜 있는지 서버에서 먼저 확인 (없으면 여기서 404)
//             if (!await fileClient.ExistsAsync())
//             {
//                 context.Response.StatusCode = 404;
//                 await context.Response.WriteAsync($"Error: File '{fileName}' not found in 'downloads' share.");
//                 return;
//             }

//             // 2. ★★★ [핵심] 10분짜리 임시 출입증(SAS) 생성 ★★★
//             // 이 부분이 없어서 아까 404가 떴던 겁니다.
//             var sasBuilder = new ShareSasBuilder
//             {
//                 ShareName = "downloads",
//                 FilePath = fileName,
//                 Resource = "f", // f = file
//                 ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(10), // 10분 유효
//                 Protocol = SasProtocol.Https
//             };
//             sasBuilder.SetPermissions(ShareFileSasPermissions.Read); // 읽기 권한 부여

//             // 3. 토큰이 포함된 진짜 다운로드 주소 생성
//             // 결과 예시: https://스토리지.file.../abc.pdf?sv=2022-11-02&sig=알수없는긴문자열...
//             Uri sasUri = fileClient.GenerateSasUri(sasBuilder);

//             Console.WriteLine($"🔗 [Download Success] SAS Token Generated. Redirecting...");

//             // 4. 리다이렉트 (이제 스토리지 문이 열립니다)
//             context.Response.Redirect(sasUri.ToString(), permanent: false);
//         }
//         catch (Exception ex)
//         {
//             Console.WriteLine($"❌ [Download Error] {ex.Message}");
//             context.Response.StatusCode = 500;
//             await context.Response.WriteAsync($"Internal Server Error: {ex.Message}");
//         }
//     });

//     // 7. 기타 필수 설정
//     webApp.MapOpenApi("/{documentName}.json");

//     var logger2 = app.Services.GetRequiredService<ILogger<Program>>();
//     logger2.LogInformation("╔════════════════════════════════════════════════════════════════╗");
//     logger2.LogInformation("║         MCP OneDrive Download Server Started                   ║");
//     logger2.LogInformation("║     Interactive Browser Authentication Enabled                ║");
//     logger2.LogInformation("║     사용자 대화형 인증이 활성화되었습니다                      ║");
//     logger2.LogInformation("╚════════════════════════════════════════════════════════════════╝");
// }

await app.RunAsync();
