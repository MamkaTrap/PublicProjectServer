namespace SHINE.Core
{
    public interface IMessage
    {
        int OpCode { get; set; }
        string SenderId { get; set; }
        string RecipientId { get; set; }

        byte[] Data { get; set; }
        byte[] DataRaw { get; set; }

        byte[] Serialize();
        void Deserialize(byte[] data);
        T GetData<T>();
    }
}
