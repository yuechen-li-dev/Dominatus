using OpenCvSharp;

namespace Dominatus.Robotics.Quadcopter3D.Shared;

public sealed record VisionFrame(long Sequence, double Timestamp, byte[] Bgr, int Width, int Height);

public sealed class VisionPipeline
{
    public VisionFrame Render(long sequence, double timestamp, float rollDegrees, float pitchDegrees, int width = 160, int height = 120)
    {
        using var frame = new Mat(height, width, MatType.CV_8UC3, Scalar.Black);
        var radians = rollDegrees * Math.PI / 180.0;
        var centerY = height / 2.0 + pitchDegrees * 1.5;
        var dx = width * .45;
        var dy = Math.Tan(radians) * dx;
        Cv2.Line(frame, new Point(width / 2.0 - dx, centerY + dy), new Point(width / 2.0 + dx, centerY - dy), Scalar.White, 3, LineTypes.AntiAlias);
        return new(sequence, timestamp, frame.ToBytes(".bmp"), width, height);
    }

    public VisionEstimate Estimate(VisionFrame frame)
    {
        using var image = Cv2.ImDecode(frame.Bgr, ImreadModes.Grayscale);
        using var edges = new Mat();
        Cv2.Canny(image, edges, 40, 120);
        var lines = Cv2.HoughLinesP(edges, 1, Math.PI / 180, 30, minLineLength: image.Width * .35, maxLineGap: 8);
        if (lines.Length == 0) return new(frame.Sequence, frame.Timestamp, 0, 0, 0, false, "no-horizon");
        var best = lines.OrderByDescending(l => Math.Abs(l.P2.X - l.P1.X)).First();
        var roll = (float)(-Math.Atan2(best.P2.Y - best.P1.Y, best.P2.X - best.P1.X) * 180 / Math.PI);
        var middleY = (best.P1.Y + best.P2.Y) * .5f;
        var pitch = (middleY - image.Height * .5f) / 1.5f;
        var confidence = Math.Clamp(Math.Abs(best.P2.X - best.P1.X) / (float)image.Width, 0, 1);
        return new(frame.Sequence, frame.Timestamp, roll, pitch, confidence, confidence > .35f, "opencv-hough");
    }
}
