using FoodScan.Api.Domain;
namespace FoodScan.Api.Services
{
    public sealed class TrieSearcher : IComponentSearchService
    {
        private readonly record struct SearchEntry(
    Component Component,
    double Score);

        private sealed class TrieNode
        {
            private const int Capacity = 10;

            private readonly Dictionary<char, TrieNode> _children = new();
            private readonly List<SearchEntry> _entries = new(Capacity + 1);

            public void Insert(string word, Component component, bool isFullName)
            {
                ArgumentNullException.ThrowIfNull(word);
                ArgumentNullException.ThrowIfNull(component);

                double coefficient = isFullName ? 1.0 : 0.8;
                TrieNode node = this;
                for (int depth = 0; ; depth++)
                {
                    double score =
                        coefficient * Math.Pow(0.95, word.Length - depth);

                    node.UpdateTop(component, score);

                    if (depth == word.Length)
                        break;

                    char symbol = word[depth];

                    if (!node._children.TryGetValue(symbol, out var child))
                    {
                        child = new TrieNode();
                        node._children.Add(symbol, child);
                    }

                    node = child;
                }
            }

            private void UpdateTop(Component component, double score)
            {
                int existingIndex = _entries.FindIndex(
                    entry => entry.Component.Id == component.Id);

                if (existingIndex >= 0)
                {
                    if (_entries[existingIndex].Score >= score)
                        return;

                    _entries.RemoveAt(existingIndex);
                }

                var candidate = new SearchEntry(component, score);

                int position = _entries.FindIndex(entry =>
                    entry.Score < score ||
                    (entry.Score == score &&
                     entry.Component.Id > component.Id));

                if (position < 0)
                    position = _entries.Count;

                _entries.Insert(position, candidate);

                if (_entries.Count > Capacity)
                    _entries.RemoveAt(Capacity);
            }

            public IEnumerable<Component> Search(string query, int limit)
            {
                ArgumentNullException.ThrowIfNull(query);

                if (limit <= 0)
                    return Array.Empty<Component>();

                TrieNode node = this;

                foreach (char symbol in query)
                {
                    if (!node._children.TryGetValue(symbol, out var child))
                        return Array.Empty<Component>();

                    node = child;
                }

                return node._entries
                    .Take(Math.Min(limit, Capacity))
                    .Select(entry => entry.Component);
            }
        }
        private readonly TrieNode _trie = new TrieNode();
        public TrieSearcher() { }
        public void Initialize(IEnumerable<Component> components)
        {
            foreach (var component in components)
            {
                AddName(component.Name, component);

                foreach (var alias in component.Aliases)
                    AddName(alias, component);
            }
        }
        private void AddName(string name, Component component)
        {
            string normalized = ComponentMatcher.Normalize(name);

            if (normalized.Length == 0)
                return;

            _trie.Insert(normalized, component, isFullName: true);

            string[] words = normalized.Split(
                ' ', StringSplitOptions.RemoveEmptyEntries);

            for (int i = 1; i < words.Length; i++)
                _trie.Insert(words[i], component, isFullName: false);
        }
        public Task<IReadOnlyList<Component>> SearchAsync(string query, int limit = 10, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string normalized = ComponentMatcher.Normalize(query);

            IReadOnlyList<Component> results = _trie
                .Search(normalized, limit)
                .ToArray();

            return Task.FromResult(results);
        }
    }
}
