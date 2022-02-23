module Rss

open FSharp.Data

type RssEntries = JsonProvider<"""{ "entries": [ { "id": "x" } ] }""", SampleIsList=true>
type Rss = XmlProvider<"https://www.reddit.com/.rss">

module RssEntries =
    let empty = RssEntries.Root([||])

    let fromEntries (entries: RssEntries.Entry[]) =
        RssEntries.Root(entries)

    let serialize (root: RssEntries.Root) =
        root.JsonValue.ToString(JsonSaveOptions.DisableFormatting)