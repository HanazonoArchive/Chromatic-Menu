namespace ChromaticMenu.Models
{
    public class TechnicalToolModel
    {
        public string Name { get; set; }
        public string Icon { get; set; }
        public string FileName { get; set; }
        public string Arguments { get; set; }
        public string ToolTip { get; set; }

        public TechnicalToolModel(string name, string icon, string fileName, string arguments = "", string toolTip = null)
        {
            Name = name;
            Icon = icon;
            FileName = fileName;
            Arguments = arguments;
            ToolTip = toolTip ?? $"Open {name}";
        }
    }
}
