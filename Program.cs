using Backend.Configuration;
using Backend.Services;
using Backend.Services.Interfaces;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

// ---- Configuración fuertemente tipada (appsettings.json + User Secrets + variables de entorno) ----
builder.Services.Configure<MongoSettings>(builder.Configuration.GetSection(MongoSettings.SectionName));
builder.Services.Configure<SqlServerSettings>(builder.Configuration.GetSection(SqlServerSettings.SectionName));
builder.Services.Configure<ConexionesLocalesSettings>(builder.Configuration.GetSection(ConexionesLocalesSettings.SectionName));

// ---- Cliente Mongo compartido ----
// Un solo IMongoClient (thread-safe, pensado para reutilizarse) para toda la app: lo usan tanto
// MongoService (colección de transacciones) como ConexionesLocalesService (colección
// "connections" con la conexión de cada local), cada uno sobre su propia base/colección dentro
// del mismo clúster.
builder.Services.AddSingleton<IMongoClient>(sp =>
{
    var mongoSettings = sp.GetRequiredService<IOptions<MongoSettings>>().Value;
    return new MongoClient(mongoSettings.ConnectionString);
});

// ---- Servicios de la aplicación ----
// Registrados como Singleton: no dependen de un DbContext por-request y ConciliacionService
// necesita mantener estado (progreso / bandera "en ejecución") entre llamadas HTTP.
// ConexionesLocalesService reemplaza a Azure (RestauranteCentralService/AzureCentralSettings) y
// a la generación dinámica de IP/BASE (ServidorService/LocalesSettings): resuelve la conexión de
// cada local (y el respaldo por restauranteId) desde la colección Mongo "connections", con
// caché en memoria para no golpear Mongo por cada local en cada conciliación. Una sola instancia
// implementa ambas interfaces para compartir el mismo caché.
builder.Services.AddSingleton<ConexionesLocalesService>();
builder.Services.AddSingleton<IServidorService>(sp => sp.GetRequiredService<ConexionesLocalesService>());
builder.Services.AddSingleton<IRestauranteCentralService>(sp => sp.GetRequiredService<ConexionesLocalesService>());
builder.Services.AddSingleton<IMongoService, MongoService>();
builder.Services.AddSingleton<ISqlServerService, SqlServerService>();
builder.Services.AddSingleton<IConciliacionService, ConciliacionService>();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Serializa los enums (EstadoConciliacion, EstadoEjecucion) como texto ("CONCILIADO",
        // "EN_PROGRESO", etc.) en vez de números, para que el frontend no dependa del orden
        // en que se declararon los valores del enum.
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Conciliación de Pagos API",
        Version = "v1",
        Description = "Conciliación de transacciones aprobadas entre MongoDB y SQL Server"
    });
});

// ---- CORS para el frontend Angular ----
const string PoliticaCorsAngular = "AngularPolicy";
var origenesPermitidos = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                          ?? new[] { "http://localhost:4200" };

builder.Services.AddCors(options =>
{
    options.AddPolicy(PoliticaCorsAngular, policy =>
    {
        policy.WithOrigins(origenesPermitidos)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Comentado temporalmente para la prueba en red/VPN interna: el certificado HTTPS de
// desarrollo solo es válido para "localhost" y no será de confianza en las PCs de los
// compañeros, y además provoca que los preflight de CORS fallen porque un redirect no
// está permitido en una solicitud preflight. Reactivar antes de producción real.
// app.UseHttpsRedirection();
app.UseCors(PoliticaCorsAngular);
app.UseAuthorization();
app.MapControllers();

// Precalienta el caché de conexiones de locales (ConexionesLocalesService) al iniciar, para que
// la primera conciliación/consulta real no pague el round-trip a Mongo. Si Mongo no está
// disponible en este momento, no se detiene el arranque de la app: el caché se intentará
// refrescar de nuevo en la primera llamada real (BuscarPorLocalAsync/ObtenerServidoresAsync).
using (var scope = app.Services.CreateScope())
{
    var conexionesLocalesService = scope.ServiceProvider.GetRequiredService<ConexionesLocalesService>();
    try
    {
        await conexionesLocalesService.RefrescarCacheAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "No se pudo precalentar el caché de conexiones de locales al iniciar; se reintentará en la primera solicitud.");
    }
}

app.Run();