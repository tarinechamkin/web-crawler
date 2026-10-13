using Microsoft.AspNetCore.Http.HttpResults;

namespace Crawler.Api.Jobs;

/// <summary>
/// REST routes. Endpoints only translate HTTP ⇄ service calls; logic lives in JobService/JobQueries.
///   POST /api/jobs                               create  → 202 { jobId }
///   GET  /api/jobs?limit=&amp;cursor=               history, newest first
///   GET  /api/jobs/{jobId}                       status + summary
///   GET  /api/jobs/{jobId}/tree                  discovery tree with ratios
///   GET  /api/jobs/{jobId}/pages/{pageId}/links  outgoing links of one page
///   POST /api/jobs/{jobId}/cancel                cancel a pending/running job
/// </summary>
public static class JobEndpoints
{
    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        var jobs = app.MapGroup("/api/jobs");

        jobs.MapPost("/", async Task<Results<AcceptedAtRoute<CreateJobResponse>, ValidationProblem, ProblemHttpResult>> (
            CreateJobRequest request, JobService service, CancellationToken ct) =>
            await service.CreateAsync(request, ct) switch
            {
                CreateJobResult.Created created => TypedResults.AcceptedAtRoute(
                    new CreateJobResponse(created.JobId), "GetJob", new { jobId = created.JobId }),
                CreateJobResult.Invalid invalid => TypedResults.ValidationProblem(invalid.Errors),
                CreateJobResult.QueueUnavailable => TypedResults.Problem(
                    "The job could not be queued. Please try again.", statusCode: StatusCodes.Status503ServiceUnavailable),
                _ => throw new InvalidOperationException("Unhandled result"),
            });

        jobs.MapGet("/", async Task<Results<Ok<JobHistoryDto>, ValidationProblem>> (
            string? cursor, int? limit, JobQueries queries, CancellationToken ct) =>
        {
            HistoryCursor? after = null;
            if (cursor is not null)
            {
                if (!HistoryCursor.TryDecode(cursor, out var decoded))
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["cursor"] = ["Invalid cursor."] });
                after = decoded;
            }
            var pageSize = Math.Clamp(limit ?? JobQueries.DefaultPageSize, 1, JobQueries.MaxPageSize);
            return TypedResults.Ok(await queries.GetHistoryAsync(after, pageSize, ct));
        });

        jobs.MapGet("/{jobId:guid}", async Task<Results<Ok<JobSummaryDto>, NotFound>> (
            Guid jobId, JobQueries queries, CancellationToken ct) =>
            await queries.GetJobAsync(jobId, ct) is { } job ? TypedResults.Ok(job) : TypedResults.NotFound())
            .WithName("GetJob");

        jobs.MapGet("/{jobId:guid}/tree", async Task<Results<Ok<JobTreeDto>, NotFound>> (
            Guid jobId, JobQueries queries, CancellationToken ct) =>
            await queries.GetTreeAsync(jobId, ct) is { } tree ? TypedResults.Ok(tree) : TypedResults.NotFound());

        jobs.MapGet("/{jobId:guid}/pages/{pageId:long}/links", async Task<Results<Ok<PageLinksDto>, NotFound>> (
            Guid jobId, long pageId, JobQueries queries, CancellationToken ct) =>
            await queries.GetPageLinksAsync(jobId, pageId, ct) is { } links ? TypedResults.Ok(links) : TypedResults.NotFound());

        jobs.MapPost("/{jobId:guid}/cancel", async Task<Results<Accepted, NotFound, ProblemHttpResult>> (
            Guid jobId, JobService service, CancellationToken ct) =>
            await service.CancelAsync(jobId, ct) switch
            {
                CancelJobResult.Canceled => TypedResults.Accepted((string?)null),
                CancelJobResult.NotFound => TypedResults.NotFound(),
                _ => TypedResults.Problem("The job has already finished.", statusCode: StatusCodes.Status409Conflict),
            });

        return app;
    }
}
