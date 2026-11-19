using EduFlow.Application.Domain;

namespace EduFlow.Tests;

public class AdmissionApplicationTests
{
    [Fact]
    public void NewApplicationStartsAsDraft()
    {
        var application = new AdmissionApplication();

        Assert.Equal(AdmissionStatus.Draft, application.Status);
        Assert.Null(application.SubmittedUtc);
    }
}
