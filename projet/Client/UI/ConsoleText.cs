using System;
using System.Collections.Generic;
using UnityEngine;

/*
 * Textes de la console en plusieurs langues. La clé est le texte français ; les valeurs suivent l'ordre
 * anglais, turc, espagnol, russe, polonais, allemand, italien. Les noms venant du jeu (cibles, munitions,
 * ressources, types de collecte) ne passent jamais ici : ils s'affichent tels que le jeu les fournit.
 */
public static class ConsoleText
{
    // Ordre d'affichage dans le volet des réglages.
    public static readonly string[] Codes = { "en", "tr", "fr", "de", "es", "pl", "ru", "it" };
    public static readonly string[] Names = { "English", "Türkçe", "Français", "Deutsch", "Español", "Polski", "Русский", "Italiano" };

    // Ordre des colonnes dans la table (le français est la clé).
    private static readonly string[] Columns = { "en", "tr", "es", "ru", "pl", "de", "it" };

    private static readonly Dictionary<string, string[]> Table = BuildTable();
    private static string _language = "en";
    private static int _column;

    public static string Language
    {
        get { return _language; }
    }

    public static void SetLanguage(string code)
    {
        _language = "en";
        _column = 0;
        if (string.IsNullOrEmpty(code))
            return;

        if (string.Equals(code, "fr", StringComparison.OrdinalIgnoreCase))
        {
            _language = "fr";
            _column = -1;
            return;
        }

        for (int i = 0; i < Columns.Length; i++)
        {
            if (string.Equals(Columns[i], code, StringComparison.OrdinalIgnoreCase))
            {
                _language = Columns[i];
                _column = i;
                return;
            }
        }
    }

    public static string T(string french)
    {
        if (_column < 0 || string.IsNullOrEmpty(french))
            return french;

        string[] row;
        return Table.TryGetValue(french, out row) ? row[_column] : french;
    }

    // Langue du système si elle est prise en charge, sinon l'anglais.
    public static string DetectSystemLanguage()
    {
        switch (Application.systemLanguage)
        {
            case SystemLanguage.French: return "fr";
            case SystemLanguage.Turkish: return "tr";
            case SystemLanguage.Spanish: return "es";
            case SystemLanguage.Russian: return "ru";
            case SystemLanguage.Polish: return "pl";
            case SystemLanguage.German: return "de";
            case SystemLanguage.Italian: return "it";
            default: return "en";
        }
    }

    private static void Add(Dictionary<string, string[]> table, string french, params string[] values)
    {
        table[french] = values;
    }

    private static Dictionary<string, string[]> BuildTable()
    {
        Dictionary<string, string[]> table = new Dictionary<string, string[]>(StringComparer.Ordinal);
        Add(table, "Paramètres", "Settings", "Ayarlar", "Ajustes", "Настройки", "Ustawienia", "Einstellungen", "Impostazioni");
        Add(table, "Santé", "Health", "Sağlık", "Salud", "Здоровье", "Zdrowie", "Gesundheit", "Salute");
        Add(table, "Cibles", "Targets", "Hedefler", "Objetivos", "Цели", "Cele", "Ziele", "Bersagli");
        Add(table, "Collecte", "Collect", "Topla", "Recolectar", "Сбор", "Zbieranie", "Sammeln", "Raccolta");
        Add(table, "Raid", "Raid", "Raid", "Raid", "Рейд", "Rajd", "Raid", "Raid");
        Add(table, "Ressources", "Resources", "Kaynaklar", "Recursos", "Ресурсы", "Zasoby", "Ressourcen", "Risorse");
        Add(table, "Annonces", "Announcements", "Duyurular", "Anuncios", "Объявления", "Ogłoszenia", "Ankündigungen", "Annunci");
        Add(table, "Activités", "Activities", "Etkinlikler", "Actividades", "Действия", "Aktywności", "Aktivitäten", "Attività");
        Add(table, "Combat", "Combat", "Savaş", "Combate", "Бой", "Walka", "Kampf", "Combattimento");
        Add(table, "Priorité", "Priority", "Öncelik", "Prioridad", "Приоритет", "Priorytet", "Priorität", "Priorità");
        Add(table, "Modules", "Modules", "Modüller", "Módulos", "Модули", "Moduły", "Module", "Moduli");
        Add(table, "Réparation", "Repair", "Onarım", "Reparación", "Ремонт", "Naprawa", "Reparatur", "Riparazione");
        Add(table, "Réapparition", "Respawn", "Yeniden doğma", "Reaparición", "Возрождение", "Odrodzenie", "Respawn", "Rinascita");
        Add(table, "Carte Raid", "Raid map", "Raid haritası", "Mapa de Raid", "Карта рейда", "Mapa rajdu", "Raid-Karte", "Mappa Raid");
        Add(table, "Coque", "Hull", "Gövde", "Casco", "Корпус", "Kadłub", "Rumpf", "Scafo");
        Add(table, "PV", "HP", "Can", "PV", "ЗД", "PŻ", "LP", "PS");
        Add(table, "En activité", "While active", "Aktifken", "En actividad", "Во время работы", "W trakcie pracy", "Während Aktivität", "In attività");
        Add(table, "À l'arrêt", "Stopped", "Dururken", "Detenido", "В остановке", "W bezruchu", "Im Stillstand", "Fermo");
        Add(table, "Réparer si PV <=", "Repair if HP <=", "Can <= ise onar", "Reparar si PV <=", "Ремонт при ЗД <=", "Napraw gdy PŻ <=", "Reparieren bei LP <=", "Ripara se PS <=");
        Add(table, "PV bas", "Low HP", "Düşük can", "PV bajos", "Мало ЗД", "Niskie PŻ", "Niedrige LP", "PS bassi");
        Add(table, "Fuite activée", "Flee enabled", "Kaçış açık", "Huida activada", "Бегство включено", "Ucieczka włączona", "Flucht aktiviert", "Fuga attivata");
        Add(table, "Fuir si PV <=", "Flee if HP <=", "Can <= ise kaç", "Huir si PV <=", "Бегство при ЗД <=", "Uciekaj gdy PŻ <=", "Fliehen bei LP <=", "Fuggi se PS <=");
        Add(table, "Collecter pendant la fuite", "Collect while fleeing", "Kaçarken topla", "Recolectar al huir", "Собирать при бегстве", "Zbieraj podczas ucieczki", "Beim Fliehen sammeln", "Raccogli durante la fuga");
        Add(table, "Cibles à PV max seulement", "Full-HP targets only", "Yalnızca tam canlı hedefler", "Solo objetivos con PV máximos", "Только цели с полным ЗД", "Tylko cele z pełnym PŻ", "Nur Ziele mit vollen LP", "Solo bersagli con PS massimi");
        Add(table, "Longue portée", "Long range", "Uzun menzil", "Largo alcance", "Дальний бой", "Daleki zasięg", "Große Reichweite", "Lunga gittata");
        Add(table, "NPC", "NPC", "NPC", "NPC", "NPC", "NPC", "NPC", "NPC");
        Add(table, "Monstres", "Monsters", "Canavarlar", "Monstruos", "Монстры", "Potwory", "Monster", "Mostri");
        Add(table, "Types", "Types", "Türler", "Tipos", "Типы", "Typy", "Typen", "Tipi");
        Add(table, "Niveau", "Level", "Seviye", "Nivel", "Уровень", "Poziom", "Level", "Livello");
        Add(table, "Boss en priorité", "Boss first", "Önce boss", "Jefe primero", "Сначала босс", "Boss najpierw", "Boss zuerst", "Prima il boss");
        Add(table, "Dégâts boss", "Boss damage", "Boss hasarı", "Daño al jefe", "Урон боссу", "Obrażenia bossa", "Boss-Schaden", "Danni al boss");
        Add(table, "Petite Raid", "Small Raid", "Küçük Raid", "Raid pequeña", "Малый рейд", "Mały rajd", "Kleiner Raid", "Raid piccolo");
        Add(table, "Grande Raid", "Large Raid", "Büyük Raid", "Raid grande", "Большой рейд", "Duży rajd", "Großer Raid", "Raid grande");
        Add(table, "Talisman", "Talisman", "Tılsım", "Talismán", "Талисман", "Talizman", "Talisman", "Talismano");
        Add(table, "mob", "mob", "mob", "mob", "моб", "mob", "Mob", "mob");
        Add(table, "boss", "boss", "boss", "jefe", "босс", "boss", "Boss", "boss");
        Add(table, "Remise à zéro", "Reset", "Sıfırla", "Reiniciar", "Сброс", "Resetuj", "Zurücksetzen", "Azzera");
        Add(table, "Navires d'événement", "Event ships", "Etkinlik gemileri", "Barcos de evento", "Корабли событий", "Statki wydarzeń", "Eventschiffe", "Navi evento");
        Add(table, "carte", "map", "harita", "mapa", "карта", "mapa", "Karte", "mappa");
        Add(table, "Aucun navire pour l'instant.", "No ships yet.", "Henüz gemi yok.", "Aún no hay barcos.", "Кораблей пока нет.", "Brak statków.", "Noch keine Schiffe.", "Nessuna nave per ora.");
        Add(table, "Aucun type pour l'instant.", "No types yet.", "Henüz tür yok.", "Aún no hay tipos.", "Типов пока нет.", "Brak typów.", "Noch keine Typen.", "Nessun tipo per ora.");
        Add(table, "En attente du joueur.", "Waiting for player.", "Oyuncu bekleniyor.", "Esperando al jugador.", "Ожидание игрока.", "Oczekiwanie na gracza.", "Warte auf Spieler.", "In attesa del giocatore.");
        Add(table, "En attente des compteurs.", "Waiting for counters.", "Sayaçlar bekleniyor.", "Esperando contadores.", "Ожидание счётчиков.", "Oczekiwanie na liczniki.", "Warte auf Zähler.", "In attesa dei contatori.");
        Add(table, "Aucun compteur n'a bougé.", "No counter changed.", "Hiçbir sayaç değişmedi.", "Ningún contador cambió.", "Ни один счётчик не изменился.", "Żaden licznik się nie zmienił.", "Kein Zähler hat sich geändert.", "Nessun contatore è cambiato.");
        Add(table, "En attente du catalogue.", "Waiting for catalog.", "Katalog bekleniyor.", "Esperando catálogo.", "Ожидание каталога.", "Oczekiwanie na katalog.", "Warte auf Katalog.", "In attesa del catalogo.");
        Add(table, "Aucun type.", "No types.", "Tür yok.", "Sin tipos.", "Нет типов.", "Brak typów.", "Keine Typen.", "Nessun tipo.");
        Add(table, "indisponible", "unavailable", "kullanılamıyor", "no disponible", "недоступно", "niedostępne", "nicht verfügbar", "non disponibile");
        Add(table, "Thème", "Theme", "Tema", "Tema", "Тема", "Motyw", "Design", "Tema");
        Add(table, "Langue", "Language", "Dil", "Idioma", "Язык", "Język", "Sprache", "Lingua");
        Add(table, "Logo", "Logo", "Logo", "Logo", "Логотип", "Logo", "Logo", "Logo");
        Add(table, "Rouge", "Red", "Kırmızı", "Rojo", "Красный", "Czerwony", "Rot", "Rosso");
        Add(table, "Blanc", "White", "Beyaz", "Blanco", "Белый", "Biały", "Weiß", "Bianco");
        Add(table, "carte inconnue", "unknown map", "bilinmeyen harita", "mapa desconocido", "неизвестная карта", "nieznana mapa", "unbekannte Karte", "mappa sconosciuta");
        Add(table, "RGB animé", "Animated RGB", "Animasyonlu RGB", "RGB animado", "RGB-анимация", "Animowane RGB", "Animiertes RGB", "RGB animato");
        return table;
    }
}
