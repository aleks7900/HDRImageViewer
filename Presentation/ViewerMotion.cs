namespace HdrImageViewer.Presentation;

internal static class ViewerMotion
{
    public const double ZoomDurationMilliseconds = 140;

    public static double Interpolate(double from, double to, double elapsedMilliseconds,
        double durationMilliseconds, bool entering = true)
    {
        var t = durationMilliseconds <= 0 ? 1 : Math.Clamp(elapsedMilliseconds / durationMilliseconds, 0, 1);
        var eased = entering ? 1 - Math.Pow(1 - t, 3) : t * t * t;
        return from + (to - from) * eased;
    }

    public static double RemainingDuration(double current, double target, double fullDistance, double fullDuration) =>
        Math.Clamp(Math.Abs(target - current) / fullDistance * fullDuration, 40, fullDuration);
}

internal sealed class RetargetableViewerMotion(double initialValue)
{
    private double _from = initialValue;
    private double _target = initialValue;
    private double _startedAt;

    public void Reset(double value)
    {
        _from = _target = value;
        _startedAt = 0;
    }

    public void Retarget(double target, double nowMilliseconds)
    {
        if (target == _target) return;
        _from = Sample(nowMilliseconds);
        _target = target;
        _startedAt = nowMilliseconds;
    }

    public double Sample(double nowMilliseconds) => ViewerMotion.Interpolate(
        _from, _target, nowMilliseconds - _startedAt, ViewerMotion.ZoomDurationMilliseconds);

    public bool IsComplete(double nowMilliseconds) =>
        _from == _target || nowMilliseconds - _startedAt >= ViewerMotion.ZoomDurationMilliseconds;
}
