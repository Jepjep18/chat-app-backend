using ChatAppBackend.Data;
using ChatAppBackend.Models;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace ChatAppBackend.Hubs
{
    public class ChatHub : Hub
    {
        private readonly ApplicationDbContext _context;

        private static ConcurrentDictionary<string, (string UserId, List<string> Interests)> ConnectedUsers = new();
        private static ConcurrentQueue<(string UserId, string ConnectionId, List<string> Interests)> WaitingUsers = new();
        private static ConcurrentDictionary<string, string> ActiveChats = new();

        public ChatHub(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task ConnectWithInterests(string userId, List<string>? interests)
        {
            if (string.IsNullOrEmpty(userId))
            {
                throw new HubException("Invalid user ID.");
            }

            interests ??= new List<string>();
            ConnectedUsers[Context.ConnectionId] = (userId, interests);
            Console.WriteLine($"User {userId} connected with interests: {(interests.Any() ? string.Join(", ", interests) : "No Interests")}");

            (string UserId, string ConnectionId, List<string> Interests) matchedUser = default;
            bool foundMatch = false;

            lock (WaitingUsers)
            {
                foreach (var user in WaitingUsers)
                {
                    if (user.Interests.Any() && interests.Any() && user.Interests.Any(i => interests.Contains(i)))
                    {
                        matchedUser = user;
                        foundMatch = true;
                        break;
                    }
                    else if (!user.Interests.Any() && !interests.Any())
                    {
                        matchedUser = user;
                        foundMatch = true;
                        break;
                    }
                }

                if (foundMatch)
                {
                    var tempQueue = new ConcurrentQueue<(string, string, List<string>)>(WaitingUsers.Where(w => w.UserId != matchedUser.UserId));
                    Interlocked.Exchange(ref WaitingUsers, tempQueue);
                }
            }

            if (foundMatch && !string.IsNullOrEmpty(matchedUser.ConnectionId))
            {
                ActiveChats[userId] = matchedUser.UserId;
                ActiveChats[matchedUser.UserId] = userId;

                await Clients.Client(Context.ConnectionId).SendAsync("Matched", matchedUser.UserId);
                await Clients.Client(matchedUser.ConnectionId).SendAsync("Matched", userId);

                Console.WriteLine($"Matched {userId} with {matchedUser.UserId}");
            }
            else
            {
                WaitingUsers.Enqueue((userId, Context.ConnectionId, interests));
                Console.WriteLine($"No match found for {userId}. Added to waiting queue.");
            }
        }

        public async Task SendMessage(string senderId, string content)
        {
            if (string.IsNullOrEmpty(senderId) || string.IsNullOrEmpty(content))
            {
                await Clients.Caller.SendAsync("Error", "Invalid sender ID or empty message.");
                return;
            }

            if (!ActiveChats.TryGetValue(senderId, out string receiverId))
            {
                await Clients.Caller.SendAsync("Error", "Cannot send message - no active chat found.");
                return;
            }

            var message = new Message
            {
                SenderId = senderId,
                ReceiverId = receiverId,
                Content = content,
                SentAt = DateTime.UtcNow
            };

            _context.Messages.Add(message);
            await _context.SaveChangesAsync();

            string receiverConnId = ConnectedUsers.FirstOrDefault(u => u.Value.UserId == receiverId).Key;

            if (!string.IsNullOrEmpty(receiverConnId))
            {
                await Clients.Client(receiverConnId).SendAsync("ReceiveMessage", senderId, content, message.SentAt);
            }

            await Clients.Caller.SendAsync("ReceiveMessage", senderId, content, message.SentAt);
        }

        public async Task Disconnect()
        {
            if (!ConnectedUsers.TryRemove(Context.ConnectionId, out var userData))
                return;

            string userId = userData.UserId;

            var tempQueue = new ConcurrentQueue<(string, string, List<string>)>(WaitingUsers.Where(u => u.UserId != userId));
            Interlocked.Exchange(ref WaitingUsers, tempQueue);

            if (ActiveChats.TryRemove(userId, out string partnerId))
            {
                ActiveChats.TryRemove(partnerId, out _);

                string partnerConnId = ConnectedUsers.FirstOrDefault(u => u.Value.UserId == partnerId).Key;
                if (!string.IsNullOrEmpty(partnerConnId))
                {
                    await Clients.Client(partnerConnId).SendAsync("PartnerDisconnected");
                    Console.WriteLine($"User {userId} disconnected. Notifying {partnerId}.");
                }
            }
            Console.WriteLine($"User {userId} disconnected.");
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await Disconnect();
            await base.OnDisconnectedAsync(exception);
        }
    }
}
