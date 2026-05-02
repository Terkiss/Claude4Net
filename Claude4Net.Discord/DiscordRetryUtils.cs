using System;
using System.Threading.Tasks;
using Discord.Net;
using Discord;

namespace Claude4Net.Discord
{
    public static class DiscordRetryUtils
    {
        public static async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action, int maxRetries = 3)
        {
            int retryCount = 0;
            while (true)
            {
                try
                {
                    return await action();
                }
                catch (HttpException ex) when ((int)ex.HttpCode == 429)
                {
                    // Rate Limit
                    retryCount++;
                    if (retryCount > maxRetries) throw;

                    // Discord.Net usually handles this internally if configured, 
                    // but we add an extra layer of safety.
                    int delay = (int)Math.Pow(2, retryCount) * 1000;
                    await Task.Delay(delay);
                }
                catch (Exception)
                {
                    retryCount++;
                    if (retryCount > maxRetries) throw;
                    await Task.Delay(1000 * retryCount);
                }
            }
        }

        public static async Task ExecuteWithRetryAsync(Func<Task> action, int maxRetries = 3)
        {
            await ExecuteWithRetryAsync<bool>(async () => 
            {
                await action();
                return true;
            }, maxRetries);
        }
    }
}
