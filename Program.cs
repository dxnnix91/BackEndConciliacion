using Backend.Configuration;
using Backend.Services;
using Backend.Services.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// ---- Configuración fuertemente tipada (appsettings.json + User Secrets + variables de entorno) ----
builder.Services.Configure<MongoSettings>(builder.Configuration.GetSection(MongoSettings.SectionName));
builder.Services.Configure<SqlServerSettings>(builder.Configuration.GetSection(SqlServerSettings.SectionName));
builder.Services.Configure<LocalesSettings>(builder.Configuration.GetSection(LocalesSettings.SectionName));
builder.Services.Configure<AzureCentralSettings>(builder.Configuration.GetSection(AzureCentralSettings.SectionName));

// ---- Servicios de la aplicación ----
// Registrados como Singleton: no dependen de un DbContext por-request y ConciliacionService
// necesita mantener estado (progreso / bandera "en ejecución") entre llamadas HTTP.
builder.Services.AddSingleton<IServidorService, ServidorService>();
builder.Services.AddSingleton<IRestauranteCentralService, RestauranteCentralService>();
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

app.Run();