namespace Sweet.Actors
{
    public interface IResponseHandler
    {
        void OnResponse(IMessage message, RemoteAddress from);
    }
}