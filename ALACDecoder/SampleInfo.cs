namespace ALACdotNET.Decoder
{
    /// <summary>
    /// Holds sample count and duration.
    /// </summary>
    internal class SampleInfo
    {
        public SampleInfo(int sampleCount, int sampleDuration)
        {
            SampleCount = sampleCount;
            SampleDuration = sampleDuration;
        }

        public int SampleCount { get; }
        public int SampleDuration { get; }
    }
}
