namespace CacheShield
{
    /// <summary>
    /// Interface for serialization and deserialization of objects.
    /// </summary>
    public interface ISerializer
    {
        byte[] Serialize<T>(T value);
        T Deserialize<T>(byte[] bytes);
    }
}
