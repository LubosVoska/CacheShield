namespace CacheShield
{
    /// <summary>
    /// Default implementation of ISerializer using MessagePack.
    /// </summary>
    public class MessagePackSerializerWrapper : ISerializer
    {
        private readonly MessagePack.MessagePackSerializerOptions _options;

        public MessagePackSerializerWrapper()
        {
            _options = MessagePack.MessagePackSerializerOptions.Standard
            .WithResolver(MessagePack.Resolvers.ContractlessStandardResolver.Instance)
            .WithCompression(MessagePack.MessagePackCompression.Lz4BlockArray)
            .WithSecurity(MessagePack.MessagePackSecurity.UntrustedData);
        }

        public byte[] Serialize<T>(T value)
        {
            return MessagePack.MessagePackSerializer.Serialize(value, _options);
        }

        public T Deserialize<T>(byte[] bytes)
        {
            return MessagePack.MessagePackSerializer.Deserialize<T>(bytes, _options);
        }
    }
}
