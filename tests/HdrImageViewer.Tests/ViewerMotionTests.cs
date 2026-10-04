using HdrImageViewer.Presentation;
using Xunit;

namespace HdrImageViewer.Tests;

public sealed class ViewerMotionTests
{
    [Fact]
    public void ContinuousRetargetingKeepsMovingAndReversalDoesNotJump()
    {
        var motion = new RetargetableViewerMotion(1);
        motion.Retarget(2, 0);
        var previous = 1.0;
        for (var time = 8; time <= 128; time += 8)
        {
            var current = motion.Sample(time);
            Assert.True(current > previous);
            motion.Retarget(2 + time / 128.0, time);
            Assert.Equal(current, motion.Sample(time));
            previous = current;
        }
        motion.Retarget(0.5, 128);
        Assert.Equal(previous, motion.Sample(128));
        Assert.True(motion.Sample(144) < previous);
        Assert.Equal(0.5, motion.Sample(268));
    }

    [Fact]
    public void RepeatedTargetAndResetDoNotLeaveAnimationRunning()
    {
        var motion = new RetargetableViewerMotion(1);
        motion.Retarget(4, 0);
        motion.Retarget(4, 100);
        Assert.True(motion.IsComplete(140));
        Assert.Equal(4, motion.Sample(140));
        motion.Reset(2);
        Assert.True(motion.IsComplete(150));
        Assert.Equal(2, motion.Sample(150));
    }

    [Theory]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(240)]
    public void ProgressDependsOnTimeRatherThanFrameCount(int refreshRate)
    {
        var frame = 1000.0 / refreshRate;
        double value = 0;
        for (var elapsed = 0.0; elapsed < 140; elapsed += frame)
        {
            var next = ViewerMotion.Interpolate(1, 4, elapsed, 140);
            Assert.InRange(next, Math.Max(1, value), 4);
            value = next;
        }
        Assert.Equal(3.625, ViewerMotion.Interpolate(1, 4, 70, 140), 10);
        Assert.Equal(4, ViewerMotion.Interpolate(1, 4, 140, 140));
    }

    [Fact]
    public void EnterAndExitRetraceTheSamePathWithoutOvershoot()
    {
        for (var time = 0; time <= 180; time++)
        {
            var enter = ViewerMotion.Interpolate(24, 0, time, 180);
            var reverseExit = ViewerMotion.Interpolate(0, 24, 180 - time, 180, entering: false);
            Assert.Equal(enter, reverseExit, 10);
            Assert.InRange(enter, 0, 24);
        }
    }

    [Fact]
    public void ReversalStartsAtCurrentPositionAndUsesRemainingDistance()
    {
        var current = ViewerMotion.Interpolate(24, 0, 90, 180);
        var duration = ViewerMotion.RemainingDuration(current, 24, 24, 180);
        Assert.Equal(current, ViewerMotion.Interpolate(current, 24, 0, duration, entering: false));
        Assert.InRange(duration, 40, 180);
        Assert.Equal(24, ViewerMotion.Interpolate(current, 24, duration, duration, entering: false));
        Assert.Equal(4, ViewerMotion.Interpolate(1, 4, 0, 0));
        Assert.Equal(4, ViewerMotion.Interpolate(1, 4, 1000, 140));
    }
}
