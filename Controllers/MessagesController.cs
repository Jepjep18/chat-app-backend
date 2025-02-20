using ChatAppBackend.Data;
using ChatAppBackend.DTOs;
using ChatAppBackend.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChatAppBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MessagesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public MessagesController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet("{userId1}/{userId2}")]
        public async Task<IActionResult> GetMessageHistory(string userId1, string userId2)
        {
            var messages = await _context.Messages
                .Where(m => (m.SenderId == userId1 && m.ReceiverId == userId2) ||
                            (m.SenderId == userId2 && m.ReceiverId == userId1))
                .OrderBy(m => m.SentAt)
                .ToListAsync();

            return Ok(messages);
        }

        [HttpPost]
        public async Task<IActionResult> SendMessage([FromBody] MessageDto messageDto)
        {
            if (messageDto == null || string.IsNullOrEmpty(messageDto.SenderId) || string.IsNullOrEmpty(messageDto.ReceiverId))
            {
                return BadRequest(new { error = "Invalid request data" });
            }

            var message = new Message
            {
                SenderId = messageDto.SenderId,
                ReceiverId = messageDto.ReceiverId,
                Content = messageDto.Content,
                SentAt = DateTime.UtcNow
            };

            _context.Messages.Add(message);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetMessageHistory), new { userId1 = messageDto.SenderId, userId2 = messageDto.ReceiverId }, message);
        }

    }
}
