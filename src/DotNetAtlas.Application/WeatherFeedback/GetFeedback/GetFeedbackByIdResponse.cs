namespace DotNetAtlas.Application.WeatherFeedback.GetFeedback;

public class GetFeedbackByIdResponse
{
    /// <summary>
    /// Unique identifier of the feedback.
    /// </summary>
    public required Guid Id { get; set; }

    /// <summary>
    /// The weather feedback content.
    /// </summary>
    public required string Feedback { get; set; }

    public required int Rating { get; set; }

    /// <summary>
    /// Who created the feedback.
    /// </summary>
    public required Guid CreatedByUser { get; set; }
}
