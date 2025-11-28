namespace SqlLoaderHelper
{
    public class SlnConfig
    {
        public static SlnConfig Instance { get; } = new SlnConfig();

        public string SQLRoot { get; set; }

        public string SqlLoaderMetaPrefix { get; set; }
    }
}
