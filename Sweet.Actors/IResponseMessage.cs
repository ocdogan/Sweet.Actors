namespace Sweet.Playbook
{
    public interface IResponseMessage
    {
        MessageId RequestId { get; }
        object Response { get; set; }
    }
}