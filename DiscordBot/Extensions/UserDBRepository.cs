using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Insight.Database;

namespace DiscordBot.Extensions
{
    public class ServerUser
    {
        /// <summary> This is internal Database ID, remember to use UserID</summary>
        // ReSharper disable once InconsistentNaming
        public int ID { get; private set; }
        // ReSharper disable once InconsistentNaming
        public string UserID { get; set; }
        public int Karma { get; set; }
        public int KarmaGiven { get; set; }
        public long Exp { get; set; }
        public int Level { get; set; }
    }
    
    public interface IServerUserRepo
    {
        [Sql("INSERT INTO users (UserID) VALUES (@UserID)")]
        Task InsertUser(ServerUser user);
        [Sql("DELETE FROM users WHERE UserID = @userId")]
        Task RemoveUser(string userId);
        
        [Sql("SELECT * FROM users WHERE UserID = @userId")]
        Task<ServerUser> GetUser(string userId);
        
        // Rank Stuff
        [Sql("SELECT UserID, Karma, Level, Exp FROM users ORDER BY Level DESC LIMIT @n")] 
        Task<IList<ServerUser>> GetTopLevel(int n);
        [Sql("SELECT UserID, Karma, KarmaGiven FROM users ORDER BY Karma DESC LIMIT @n")] 
        Task<IList<ServerUser>> GetTopKarma(int n);
        [Sql("SELECT COUNT(UserID)+1 FROM users WHERE Level > @level")] 
        Task<long> GetLevelRank(string userId, int level);
        [Sql("SELECT COUNT(UserID)+1 FROM users WHERE Karma > @karma")] 
        Task<long> GetKarmaRank(string userId, int karma);
        
        // Update Values
        [Sql("UPDATE users SET Karma = @karma WHERE UserID = @userId")] 
        Task UpdateKarma(string userId, int karma);
        [Sql("UPDATE users SET KarmaGiven = @karmaGiven WHERE UserID = @userId")] 
        Task UpdateKarmaGiven(string userId, int karmaGiven);
        [Sql("UPDATE users SET Exp = @xp WHERE UserID = @userId")] 
        Task UpdateXp(string userId, long xp);
        [Sql("UPDATE users SET Level = @level WHERE UserID = @userId")] 
        Task UpdateLevel(string userId, int level);
        
        // Get Single Values
        [Sql("SELECT Karma FROM users WHERE UserID = @userId")] 
        Task<int> GetKarma(string userId);
        [Sql("SELECT KarmaGiven FROM users WHERE UserID = @userId")] 
        Task<int> GetKarmaGiven(string userId);
        [Sql("SELECT Exp FROM users WHERE UserID = @userId")] 
        Task<long> GetXp(string userId);
        [Sql("SELECT Level FROM users WHERE UserID = @userId")] 
        Task<int> GetLevel(string userId);

        /// <summary>Returns a count of users in the Table, otherwise it fails. </summary>
        [Sql("SELECT COUNT(*) FROM users")]
        long TestConnection();
    }
}