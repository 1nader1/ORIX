using DesignToAngular.Services;
using DesignToAngular.Services.CodeGen;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient<DesignAnalyzer>(c => c.Timeout = TimeSpan.FromMinutes(4));
builder.Services.AddSingleton<CodeGenerator>();
builder.Services.AddCors(o => o.AddPolicy("web", p =>p.WithOrigins("http://localhost:4200").AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("web");
app.MapControllers();

app.Run();  