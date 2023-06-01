using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ScottPlot;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<PvDbContext>(options =>
    options.UseSqlServer(builder.Configuration["ConnectionStrings:DefaultConnection"]));
var app = builder.Build();


app.MapGet("/", () => "Hello World!");

app.MapPost("/installations", async (PvInstallationDto installationDto, PvDbContext dbContext) => {

    if (installationDto.Longitude is < -180 or > 180) { return Results.BadRequest("Longitude must be between -180 and 180"); }
    if (installationDto.Latitude is < -90 or > 90) { return Results.BadRequest("Latitude must be between -90 and 90"); }
    if (string.IsNullOrWhiteSpace(installationDto.Address)) { return Results.BadRequest("Address must be provided"); }
    if (string.IsNullOrWhiteSpace(installationDto.OwnerName)) { return Results.BadRequest("Owner name must be provided"); }
    if (installationDto.Address.Length > 1024) { return Results.BadRequest("Address must be less than 1024 characters"); }
    if (installationDto.OwnerName.Length > 512) { return Results.BadRequest("Owner name must be less than 512 characters"); }
    if (!string.IsNullOrWhiteSpace(installationDto.Comments) && installationDto.Comments.Length > 1024) { return Results.BadRequest("Comments must be less than 1024 characters"); }


    var pvInstallation = new PvInstallation
    {
        Longitude = installationDto.Longitude,
        Latitude = installationDto.Latitude,
        Address = installationDto.Address,
        OwnerName = installationDto.OwnerName,
        Comments = installationDto.Comments
    };
    await dbContext.PvInstallations.AddAsync(pvInstallation);
    await dbContext.InstallationLogs.AddAsync(new()
    {
        Action = "Create",
        Timestamp = DateTime.UtcNow,
        PvInstallation = pvInstallation,
        NewValue = JsonSerializer.Serialize(pvInstallation)
    });
    await dbContext.SaveChangesAsync();
    return Results.Json(pvInstallation,statusCode:StatusCodes.Status201Created);
});

app.MapPost("/installations/{id}/deactivate", async (PvDbContext dbContext, int id) => {
    var pvInstallation = await dbContext.PvInstallations.FindAsync(id);
    if (pvInstallation is null) { return Results.NotFound(); }
    pvInstallation.IsActive = false;
        
    await dbContext.InstallationLogs.AddAsync(new()
    {
        Action = "Activate/Deactivate",
        Timestamp = DateTime.UtcNow,
        PvInstallation = pvInstallation,
        PreviousValue = "true",
        NewValue = "false"
    });
    await dbContext.SaveChangesAsync();
    return Results.Ok(pvInstallation);
});

app.MapPost("/installations/{id}/reports", async (ProductionReportDto report, PvDbContext dbContext, int id) => {
    var pvInstallation = await dbContext.PvInstallations.FindAsync(id);
        if (report.ProducedWattage < 0) { return Results.BadRequest("Produced wattage must be greater than or equal to 0"); }
    if (report.HouseholdWattage < 0) { return Results.BadRequest("Household wattage must be greater than or equal to 0"); }
    if (report.BatteryWattage < 0) { return Results.BadRequest("Battery wattage must be greater than or equal to 0"); }
    if (report.GridWattage < 0) { return Results.BadRequest("Grid wattage must be greater than or equal to 0"); }
    if (pvInstallation is null) { return Results.NotFound(); }
    var productionReport = new ProductionReport
    {
        PvInstallationId = id,
        ProducedWattage = report.ProducedWattage,
        HouseholdWattage = report.HouseholdWattage,
        BatteryWattage = report.BatteryWattage,
        GridWattage = report.GridWattage,
        Timestamp = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, DateTime.UtcNow.Hour, DateTime.UtcNow.Minute, 0)    
    };
    await dbContext.ProductionReports.AddAsync(productionReport);
    await dbContext.SaveChangesAsync();
    return Results.Json(productionReport, statusCode: StatusCodes.Status201Created);
});

app.MapGet("/installations/{id}/reports", async (PvDbContext dbContext, int id, DateTime start, int duration) => {
    var pvInstallation = await dbContext.PvInstallations.FindAsync(id);
    if (pvInstallation is null) { return Results.NotFound(); }
    var end = start.AddMinutes(duration);
    var sum = await dbContext.ProductionReports
        .Where(r => r.PvInstallationId == id && r.Timestamp >= start && r.Timestamp <= end)
        .SumAsync(r => r.ProducedWattage);
    return Results.Json(sum);
});

app.Run();
record PvInstallationDto(float Longitude, float Latitude, string Address, string OwnerName, string Comments);
record ProductionReportDto(float ProducedWattage, float HouseholdWattage, float BatteryWattage, float GridWattage);