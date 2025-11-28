from twitchio.ext import commands
import requests

# TWITCH CONFIG
TOKEN = 'oauth:your_token_here' # Get from https://twitchtokengenerator.com/
CHANNEL = 'your_channel_name'
BRAIN_URL = "http://127.0.0.1:5000/trigger"

class Bot(commands.Bot):
    def __init__(self):
        super().__init__(token=TOKEN, prefix='!', initial_channels=[CHANNEL])

    async def event_ready(self):
        print(f'Logged in as | {self.nick}')

    @commands.command(name='chaos')
    async def chaos(self, ctx: commands.Context, *, prompt: str):
        # Optional: Add logic to check for bits/subs/channel points here
        
        print(f"Chat request: {prompt}")
        await ctx.send(f"@{ctx.author.name} submitted a chaos event! Processing...")
        
        # Send to our brain script
        try:
            requests.post(BRAIN_URL, json={"prompt": prompt})
        except:
            await ctx.send("The AI Brain is offline!")

bot = Bot()
bot.run()