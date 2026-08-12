using System.Windows.Media;

namespace ChuniLauncher.Net
{
    public class GameItem
    {
        public string Name { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public string LogoPath { get; set; } = string.Empty;
        public string ColorString { get; set; } = "#FFFFFF";
        public string Category { get; set; } = "Uncategorized";

        public Brush TextColor
        {
            get
            {
                try
                {
                    return (Brush)new BrushConverter().ConvertFromString(ColorString);
                }
                catch
                {
                    return Brushes.White;
                }
            }
        }
    }
}
