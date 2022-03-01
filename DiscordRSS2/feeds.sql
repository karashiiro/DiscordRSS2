CREATE TABLE IF NOT EXISTS feeds (
    ID           INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
    FEED_KEY     TEXT                              NOT NULL,
    FEED_GROUP   TEXT                              NOT NULL,
    FEED_URL     TEXT                              NOT NULL,
    FEED_CHANNEL TEXT                              NOT NULL
)