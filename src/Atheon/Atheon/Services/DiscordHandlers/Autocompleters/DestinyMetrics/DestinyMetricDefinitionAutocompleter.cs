﻿using Atheon.Extensions;
using Atheon.Services.Interfaces;
using Discord;
using Discord.Interactions;
using DotNetBungieAPI.Models.Destiny.Definitions.Metrics;

namespace Atheon.Services.DiscordHandlers.Autocompleters.DestinyMetrics
{
    public class DestinyMetricDefinitionAutocompleter : AutocompleteHandler
    {
        private readonly IBungieClientProvider _bungieClientProvider;
        private readonly ILogger<DestinyMetricDefinitionAutocompleter> _logger;
        private readonly IDestinyDb _destinyDb;
        private readonly IMemoryCache _memoryCache;

        public DestinyMetricDefinitionAutocompleter(
            IBungieClientProvider bungieClientProvider,
            ILogger<DestinyMetricDefinitionAutocompleter> logger,
            IDestinyDb destinyDb,
            IMemoryCache memoryCache)
        {
            _bungieClientProvider = bungieClientProvider;
            _logger = logger;
            _destinyDb = destinyDb;
            _memoryCache = memoryCache;
        }

        public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
            IInteractionContext context,
            IAutocompleteInteraction autocompleteInteraction,
            IParameterInfo parameter, IServiceProvider services)
        {
            try
            {
                var lang = await _memoryCache.GetOrAddAsync(
                    $"guild_lang_{context.Guild.Id}",
                    async () => (await _destinyDb.GetGuildLanguageAsync(context.Guild.Id)).ConvertToBungieLocale(),
                    TimeSpan.FromSeconds(15),
                    Caching.CacheExpirationType.Absolute);

                var client = await _bungieClientProvider.GetClientAsync();
                var searchEntry = (string)autocompleteInteraction.Data.Options.First(x => x.Focused).Value;

                var searchResults = client
                    .Repository
                    .GetAll<DestinyMetricDefinition>(lang)
                    .Where(x =>
                    {
                        return x.DisplayProperties.Name.Contains(searchEntry, StringComparison.InvariantCultureIgnoreCase);
                    })
                    .Take(20);

                var results = searchResults
                    .Where(x => x.DisplayProperties.Name.Length > 0)
                    .Select(x => new AutocompleteResult(x.DisplayProperties.Name, x.Hash.ToString()));

                return !results.Any() ? AutocompletionResult.FromSuccess() : AutocompletionResult.FromSuccess(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to form collectibles for query");
                return AutocompletionResult.FromSuccess();
            }
        }
    }
}
