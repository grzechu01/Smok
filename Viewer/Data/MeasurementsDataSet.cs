using System.Drawing;

namespace SmokViewer.Data
{
    public record MeasurementsDataSet(string Caption, Color Color, IEnumerable<(DateTime date, double value)> Data);
}
