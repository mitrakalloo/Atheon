﻿using Discord.Interactions;
using Discord.WebSocket;

namespace Atheon.Services.Discord.InteractionHandlers.Base
{
    public abstract class SlashCommandHandlerBase : InteractionModuleBase<ShardedInteractionContext<SocketSlashCommand>>
    {
        private readonly ILogger _logger;

        public SlashCommandHandlerBase(ILogger logger)
        {
            _logger = logger;
        }

        public override void BeforeExecute(ICommandInfo command)
        {
            _logger.LogInformation("Started executing command: {CommandName}", command.Name);
        }

        public override void AfterExecute(ICommandInfo command)
        {
            _logger.LogInformation("Finished executing command: {CommandName}", command.Name);
        }
    }
}
