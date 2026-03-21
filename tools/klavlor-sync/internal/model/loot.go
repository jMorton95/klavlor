package model

// LootRecord mirrors the KlavLor LootIngestCommand DTO exactly.
type LootRecord struct {
	Name        string     `json:"name"`
	Level       int        `json:"level"`
	KillCount   int        `json:"killCount"`
	Type        string     `json:"type"`
	Drops       []LootDrop `json:"drops"`
	Date        string     `json:"date"`
	ContentHash string     `json:"contentHash,omitempty"`
	Imported    bool       `json:"imported,omitempty"`
	CharacterId string     `json:"characterId,omitempty"`
}

// LootDrop mirrors the KlavLor LootDropDto.
type LootDrop struct {
	Name     string `json:"name"`
	Id       int    `json:"id"`
	Quantity int    `json:"quantity"`
	Price    int    `json:"price"`
}
