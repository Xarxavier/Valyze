using Microsoft.AspNetCore.Mvc;
using Valyze.Domain.Application.Ingestion;
using Valyze.Domain.Entities.Identity;

namespace Valyze.Host.MinimalApi.Trades;

public static class TradesEndpoints
{
    private const long MaxFileBytes = 10 * 1024 * 1024;

    public static RouteGroupBuilder MapTradesEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/trades")
            .WithTags("Trades")
            .RequireAuthorization()
            .DisableAntiforgery();

        group.MapPost("/import", async (
            [FromForm] IFormFile file,
            [FromQuery] string broker,
            IImportTradesUseCase useCase,
            AccessorClassEntity accessor,
            CancellationToken ct) =>
        {
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { code = "msnFileRequired" });
            if (file.Length > MaxFileBytes)
                return Results.BadRequest(new { code = "msnFileTooLarge", detail = $"Max {MaxFileBytes} bytes." });
            if (!file.ContentType.Contains("pdf", StringComparison.OrdinalIgnoreCase)
                && !file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { code = "msnFileNotPdf" });

            await using var stream = file.OpenReadStream();
            var result = await useCase.ExecuteAsync(
                accessor.AccountId,
                broker,
                stream,
                file.FileName,
                ct);

            return Results.Ok(new
            {
                fileName = result.FileName,
                brokerKey = result.BrokerKey,
                tradesImported = result.TradesImported,
                tradesSkipped = result.TradesSkipped,
                warnings = result.Warnings,
                rawTextSample = result.RawTextSample,
            });
        })
        .WithName("ImportTrades")
        .DisableAntiforgery()
        .Accepts<IFormFile>("multipart/form-data");

        return group;
    }
}
