namespace Sweet.Actors
{
    public class ServerEndPoint
    {
        public ServerEndPoint(string host, int port)
        {
            Host = host?.Trim();
            Port = port;
        }

        public string Host { get; }

        public int Port { get; }
    }
}
