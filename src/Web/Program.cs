using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Yapılandırma: appsettings.json → config/local.json (model seçimleri vb.) → ortam değişkenleri.
builder.AddDevAgentsConfiguration();

// Katmanları içten dışa kaydediyoruz (Clean Architecture şablonundaki sırayla).
builder.AddApplicationServices();
builder.AddInfrastructureServices();
builder.AddWebServices();

var app = builder.Build();

// Uç noktalarda fırlayan hatalar ProblemDetailsExceptionHandler ile HTTP cevabına çevrilir.
app.UseExceptionHandler(options => { });

// wwwroot: web sohbet arayüzü (index.html).
app.UseFileServer();

// API dokümantasyonu: /openapi/v1.json ve /scalar.
app.MapOpenApi();
app.MapScalarApiReference();

// Endpoints/ klasöründeki tüm IEndpointGroup sınıflarını bulup kaydediyoruz.
app.MapEndpoints(typeof(Program).Assembly);

app.Run();

// Fonksiyonel testler (WebApplicationFactory) Program sınıfına erişebilsin diye.
public partial class Program;
