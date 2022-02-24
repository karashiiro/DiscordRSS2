module Rss

open FSharp.Data

type Rss = XmlProvider<"https://www.reddit.com/.rss">