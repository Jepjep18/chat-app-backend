namespace ChatAppBackend.DTOs
{
    public class MessageDto
    {
        public string SenderId { get; set; }
        public string ReceiverId { get; set; }
        public string Content { get; set; }
    }

}
