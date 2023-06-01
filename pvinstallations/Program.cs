using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ScottPlot;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<PvDbContext>(options =>
    options.UseSqlServer(builder.Configuration["ConnectionStrings:DefaultConnection"]));
var app = builder.Build();


app.MapGet("/", () => "Hello World!");

app.MapPost("/installations", async (PvInstallationDto installationDto, PvDbContext dbContext) => {


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

/*
Create an API with the following endpoints:

POST /installations: An endpoint that accepts a JSON payload with the longitude, latitude, address, owner name, and optional comments of a new installation. This endpoint should create a new PvInstallation and return at least its Id (you can return the entire installation record if you want).
POST /installations/{id}/deactivate: An endpoint that sets an installation's IsActive flag to false.
POST /installations/{id}/reports: An endpoint that accepts a JSON payload with produced wattage, household wattage, battery wattage, and grid wattage. The timestamp is filled with the system time (current UTC system time truncated to the current minute; e.g. 2023-05-25T17:02:30 becomes 2023-05-25T17:02:00). This endpoint should create a new ProductionReport for the installation with the provided Id.
GET /installations/{id}/reports: An endpoint that accepts query parameters for a start timestamp and a duration in minutes. This endpoint should return the sum of the ProducedWattage of the installation with the provided Id during the specified period.
*/

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