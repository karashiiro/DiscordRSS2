CREATE TABLE IF NOT EXISTS feeds (
    feed_key     TEXT PRIMARY KEY                  NOT NULL,
    feed_group   TEXT                              NOT NULL,
    feed_url     TEXT                              NOT NULL,
    feed_channel TEXT                              NOT NULL
);

CREATE TABLE IF NOT EXISTS seen (
    id           INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
    seen_list    TEXT                              NOT NULL,
    seen_feed    TEXT                              NOT NULL
);