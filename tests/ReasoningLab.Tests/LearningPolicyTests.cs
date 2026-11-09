using ReasoningLab.Core;

namespace ReasoningLab.Tests;

public class LearningPolicyTests
{
    [Fact]
    public void Three_distinct_successes_unlock_next_level()
    {
        var now = DateTimeOffset.UtcNow;
        var state = LearningPolicy.Advance(1, 0, true, 1, false, now);
        state = LearningPolicy.Advance(state.Level, state.Streak, true, 1, false, now);
        Assert.Equal(1, state.Level);
        state = LearningPolicy.Advance(state.Level, state.Streak, true, 1, false, now);
        Assert.Equal(2, state.Level);
        Assert.Equal(now.AddDays(2), state.ReviewDue);
    }
    [Theory]
    [InlineData(true, 1, true)]
    [InlineData(true, 1, false)]
    [InlineData(false, 2, false)]
    public void Reviews_easy_items_and_errors_do_not_unlock_levels(bool correct, int difficulty, bool seen)
    {
        var result = LearningPolicy.Advance(2, 2, correct, difficulty, seen, DateTimeOffset.UtcNow);
        Assert.Equal(2, result.Level);
        Assert.Equal(0, result.Streak);
    }
}
