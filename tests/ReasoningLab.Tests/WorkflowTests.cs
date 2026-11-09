using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ReasoningLab.Core;
using ReasoningLab.Server.Persistence;

namespace ReasoningLab.Tests;

public class WorkflowTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Delayed_retry_cannot_answer_a_later_review_of_the_same_question()
    {
        using var client = await Learner();
        var session = await Start(client);
        var original = new SubmitAnswer(session.Question!.Id, await fixture.CorrectAnswer(session.Question.Id));
        for (var i = 0; i < 7; i++) session = await Correct(client, session);
        fixture.Clock.Now = fixture.Clock.Now.AddDays(8);
        session = await Start(client);
        Assert.True(session.Question!.IsReview);
        Assert.NotEqual(original.QuestionId, session.Question.Id);
        var reviewId = session.Question.Id;
        var retried = await Read(await client.PostAsJsonAsync($"/api/sessions/{session.Id}/answers", original));
        Assert.Equal(reviewId, retried.Question!.Id);
        Assert.Equal(7, Assert.Single((await client.GetFromJsonAsync<Dashboard>("/api/dashboard"))!.Skills).Attempts);
    }

    private async Task<HttpClient> Learner()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-ReasoningLab", "1");
        (await client.PostAsJsonAsync("/api/profile", new { })).EnsureSuccessStatusCode();
        return client;
    }
    private static async Task<SessionView> Start(HttpClient client, string pathway = "general", string mode = "practice") =>
        await Read(await client.PostAsJsonAsync("/api/sessions", new StartSession(pathway, mode)));
    private static async Task<SessionView> Read(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        return JsonSerializer.Deserialize<SessionView>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
    private async Task<SessionView> Correct(HttpClient c, SessionView s) => await Read(await c.PostAsJsonAsync($"/api/sessions/{s.Id}/answers", new SubmitAnswer(s.Question!.Id, await fixture.CorrectAnswer(s.Question.Id))));

    [Fact]
    public async Task Acceptance_workflow_persists_mastery_adapts_and_keeps_exam_results_separate()
    {
        using var c = await Learner();
        var s = await Start(c);
        for (var i = 0; i < 3; i++) { Assert.Equal(1, s.Question!.Difficulty); s = await Correct(c, s); }
        Assert.Equal(2, s.Question!.Difficulty);
        var before = (await c.GetFromJsonAsync<Dashboard>("/api/dashboard"))!;
        Assert.Equal(2, Assert.Single(before.Skills).Level);
        var resumed = await Start(c);
        Assert.Equal(s.Id, resumed.Id);
        Assert.Equal(s.Question.Id, resumed.Question!.Id);
        var lsat = await Start(c, "lsat");
        Assert.Equal(2, lsat.Question!.Difficulty);
        lsat = await Correct(c, lsat);
        var ucat = await Start(c, "ucat");
        Assert.Equal(2, ucat.Question!.Difficulty);
        var diagnostic = await Start(c, "lsat", "diagnostic");
        Assert.Equal(3, diagnostic.Total);
        for (var i = 0; i < 3; i++) { diagnostic = await Correct(c, diagnostic); Assert.Null(diagnostic.Feedback); }
        Assert.True(diagnostic.Completed);
        Assert.Equal(3, diagnostic.Result!.Correct);
        var after = (await c.GetFromJsonAsync<Dashboard>("/api/dashboard"))!;
        Assert.Equal(4, Assert.Single(after.Skills).Attempts);
        Assert.Equal(3, Assert.Single(after.Results).Correct);
        Assert.NotEmpty(after.Recommendations);
        Assert.Equal(2, after.Pathways.Length);
        // A fresh EF context reads the durable evidence, not a service's in-memory state.
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LabDbContext>();
        Assert.True(await db.Evidence.CountAsync() >= 4);
    }

    [Fact]
    public async Task Duplicate_concurrent_submissions_record_one_attempt()
    {
        using var c = await Learner(); var s = await Start(c);
        var answer = new SubmitAnswer(s.Question!.Id, await fixture.CorrectAnswer(s.Question.Id));
        var requests = Enumerable.Range(0, 5).Select(_ => c.PostAsJsonAsync($"/api/sessions/{s.Id}/answers", answer));
        foreach (var response in await Task.WhenAll(requests)) response.EnsureSuccessStatusCode();
        Assert.Equal(1, Assert.Single((await c.GetFromJsonAsync<Dashboard>("/api/dashboard"))!.Skills).Attempts);
        var conflict = await c.PostAsJsonAsync($"/api/sessions/{s.Id}/answers", answer with { Choice = (answer.Choice + 1) % 3 });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task Learners_cannot_access_other_sessions_or_leak_answers()
    {
        using var a = await Learner(); using var b = await Learner(); var s = await Start(a);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/sessions/{s.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsJsonAsync($"/api/sessions/{s.Id}/answers", new SubmitAnswer(s.Question!.Id, 0))).StatusCode);
        var json = await a.GetStringAsync($"/api/sessions/{s.Id}");
        Assert.DoesNotContain("correctChoice", json); Assert.DoesNotContain("explanation", json);
        Assert.Empty((await b.GetFromJsonAsync<Dashboard>("/api/dashboard"))!.Skills);
        using var anonymous = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/dashboard")).StatusCode);
    }

    [Fact]
    public async Task Invalid_answers_and_cross_origin_mutations_are_rejected()
    {
        using var c = await Learner(); var s = await Start(c);
        foreach (var answer in new[] { new SubmitAnswer(Guid.NewGuid(), 0), new SubmitAnswer(s.Question!.Id, -1), new SubmitAnswer(s.Question.Id, 99) })
            Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"/api/sessions/{s.Id}/answers", answer)).StatusCode);
        c.DefaultRequestHeaders.Add("Origin", "https://untrusted.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/sessions", new StartSession("general"))).StatusCode);
    }

    [Fact]
    public async Task Expiry_uses_server_time_and_counts_unanswered_items_as_incorrect()
    {
        using var c = await Learner(); var s = await Start(c, "lsat", "diagnostic");
        s = await Correct(c, s);
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(4);
        var expired = await Correct(c, s);
        Assert.True(expired.Completed); Assert.Equal(1, expired.Result!.Correct); Assert.Equal(3, expired.Result.Total);
        Assert.Equal(1, Assert.Single((await c.GetFromJsonAsync<Dashboard>("/api/dashboard"))!.Results).Correct);
    }

    [Fact]
    public async Task Fixed_form_is_independent_of_mastery_and_exam_dates_select_versions()
    {
        using var a = await Learner(); using var b = await Learner(); var p = await Start(a); for (int i = 0; i < 3; i++) p = await Correct(a, p);
        Assert.Equal((await Start(a, "lsat", "diagnostic")).Question!.Prompt, (await Start(b, "lsat", "diagnostic")).Question!.Prompt);
        Assert.Equal("lsat-2024", (await a.GetFromJsonAsync<BlueprintInfo>("/api/blueprints/lsat?on=2027-07-31"))!.Id);
        Assert.Equal("lsat-2027", (await a.GetFromJsonAsync<BlueprintInfo>("/api/blueprints/lsat?on=2027-08-01"))!.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync("/api/sessions", new StartSession("ucat", "diagnostic"))).StatusCode);
    }

    [Fact]
    public async Task Wrong_answer_records_misconception_and_due_review_does_not_promote()
    {
        using var c = await Learner(); var s = await Start(c); var first = s.Question!;
        s = await Read(await c.PostAsJsonAsync($"/api/sessions/{s.Id}/answers", new SubmitAnswer(first.Id, (await fixture.CorrectAnswer(first.Id) + 1) % first.Options.Length)));
        Assert.False(s.Feedback!.Correct);
        for (int i = 0; i < 6; i++) s = await Correct(c, s);
        Assert.True(s.Exhausted);
        var before = (await c.GetFromJsonAsync<Dashboard>("/api/dashboard"))!;
        Assert.NotEmpty(before.Misconceptions);
        fixture.Clock.Now = fixture.Clock.Now.AddDays(8);
        s = await Start(c); Assert.True(s.Question!.IsReview); s = await Correct(c, s);
        Assert.Equal(before.Skills[0].Level, (await c.GetFromJsonAsync<Dashboard>("/api/dashboard"))!.Skills[0].Level);
    }

    [Fact]
    public async Task Administration_requires_token_and_appends_immutable_versions()
    {
        using var c = await Learner();
        var q = new NewQuestion("test-" + Guid.NewGuid(), "general", "critical", 1, "All A are B. X is A. Is X B?", ["Yes", "No"], 0, "The universal rule includes X.", "Use the stated premises.");
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/admin/questions", q)).StatusCode);
        c.DefaultRequestHeaders.Add("X-Admin-Token", "integration-test-token");
        var first = await c.PostAsJsonAsync("/api/admin/questions", q); Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var second = await c.PostAsJsonAsync("/api/admin/questions", q with { Explanation = "A revised explanation of the same rule." });
        Assert.Equal(2, (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/admin/questions", q with { Answer = 99 })).StatusCode);
    }
}
