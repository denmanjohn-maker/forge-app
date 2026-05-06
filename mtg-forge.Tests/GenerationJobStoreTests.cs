using MtgForge.Api.Services;

namespace MtgForge.Tests;

public class GenerationJobStoreTests
{
    [Fact]
    public void Create_Returns_Pending_Job_For_User()
    {
        var store = new GenerationJobStore();
        var job = store.Create("user1");
        Assert.Equal(GenerationJobStatus.Pending, job.Status);
        Assert.Equal("user1", job.UserId);
        Assert.NotEmpty(job.Id);
    }

    [Fact]
    public void Get_Returns_Job_By_Id()
    {
        var store = new GenerationJobStore();
        var job = store.Create("user1");
        var result = store.Get(job.Id);
        Assert.NotNull(result);
        Assert.Equal(job.Id, result.Id);
    }

    [Fact]
    public void Get_Returns_Null_For_Unknown_Id()
    {
        var store = new GenerationJobStore();
        Assert.Null(store.Get("nonexistent"));
    }

    [Fact]
    public void Status_Changes_Are_Reflected_Via_Get()
    {
        var store = new GenerationJobStore();
        var job = store.Create("user1");

        job.Status = GenerationJobStatus.Running;
        Assert.Equal(GenerationJobStatus.Running, store.Get(job.Id)!.Status);

        job.Status = GenerationJobStatus.Completed;
        Assert.Equal(GenerationJobStatus.Completed, store.Get(job.Id)!.Status);
    }

    [Fact]
    public void Multiple_Jobs_Are_Independently_Tracked()
    {
        var store = new GenerationJobStore();
        var j1 = store.Create("user1");
        var j2 = store.Create("user2");

        j1.Status = GenerationJobStatus.Failed;
        j2.Status = GenerationJobStatus.Completed;

        Assert.Equal(GenerationJobStatus.Failed, store.Get(j1.Id)!.Status);
        Assert.Equal(GenerationJobStatus.Completed, store.Get(j2.Id)!.Status);
    }

    [Fact]
    public void Job_Deck_And_Error_Are_Visible_After_Set()
    {
        var store = new GenerationJobStore();
        var job = store.Create("user1");
        job.Error = "oops";
        job.Status = GenerationJobStatus.Failed;

        var result = store.Get(job.Id)!;
        Assert.Equal("oops", result.Error);
        Assert.Equal(GenerationJobStatus.Failed, result.Status);
    }
}
