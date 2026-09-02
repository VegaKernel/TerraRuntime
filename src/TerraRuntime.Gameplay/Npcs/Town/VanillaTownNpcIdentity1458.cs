using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

public readonly record struct VanillaTownNpcSpawnIdentity1458(string GivenName, int VariationIndex);

public interface IVanillaTownNpcRandom1458
{
    int Next(int exclusiveMax);
}

public sealed class SystemVanillaTownNpcRandom1458 : IVanillaTownNpcRandom1458
{
    private readonly Random random;
    public SystemVanillaTownNpcRandom1458(Random? random = null) => this.random = random ?? new Random();
    public int Next(int exclusiveMax) => random.Next(exclusiveMax);
}

public interface IVanillaTownNpcNameCatalog1458
{
    string RandomFromCategory(string category, IVanillaTownNpcRandom1458 random);
}

/// <summary>
/// Terraria 1.4.5.8 en-US Town localization name categories consumed by NPC.getNewNPCName and VariantNPCProfile.
/// TerraRuntime currently owns the vanilla default en-US server locale for generated given names; announcement text
/// remains client-localized through NetworkText.
/// </summary>
public sealed class VanillaEnglishTownNpcNameCatalog1458 : IVanillaTownNpcNameCatalog1458
{
    public static readonly VanillaEnglishTownNpcNameCatalog1458 Instance = new();
    private VanillaEnglishTownNpcNameCatalog1458() { }

    public string RandomFromCategory(string category, IVanillaTownNpcRandom1458 random)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(random);
        string[] values = category switch
        {
            "MerchantNames" => MerchantNames,
            "NurseNames" => NurseNames,
            "ArmsDealerNames" => ArmsDealerNames,
            "DryadNames" => DryadNames,
            "GuideNames" => GuideNames,
            "DemolitionistNames" => DemolitionistNames,
            "ClothierNames" => ClothierNames,
            "GoblinTinkererNames" => GoblinTinkererNames,
            "WizardNames" => WizardNames,
            "MechanicNames" => MechanicNames,
            "TruffleNames" => TruffleNames,
            "SteampunkerNames" => SteampunkerNames,
            "DyeTraderNames" => DyeTraderNames,
            "PartyGirlNames" => PartyGirlNames,
            "CyborgNames" => CyborgNames,
            "PainterNames" => PainterNames,
            "WitchDoctorNames" => WitchDoctorNames,
            "PirateNames" => PirateNames,
            "StylistNames" => StylistNames,
            "TravelingMerchantNames" => TravelingMerchantNames,
            "AnglerNames" => AnglerNames,
            "SkeletonMerchantNames" => SkeletonMerchantNames,
            "TaxCollectorNames" => TaxCollectorNames,
            "BartenderNames" => BartenderNames,
            "GolferNames" => GolferNames,
            "BestiaryGirlNames" => BestiaryGirlNames,
            "PrincessNames" => PrincessNames,
            "CatNames_Siamese" => CatNames_Siamese,
            "CatNames_Black" => CatNames_Black,
            "CatNames_OrangeTabby" => CatNames_OrangeTabby,
            "CatNames_RussianBlue" => CatNames_RussianBlue,
            "CatNames_Silver" => CatNames_Silver,
            "CatNames_White" => CatNames_White,
            "DogNames_Labrador" => DogNames_Labrador,
            "DogNames_PitBull" => DogNames_PitBull,
            "DogNames_Beagle" => DogNames_Beagle,
            "DogNames_Corgi" => DogNames_Corgi,
            "DogNames_Dalmation" => DogNames_Dalmation,
            "DogNames_Husky" => DogNames_Husky,
            "BunnyNames_White" => BunnyNames_White,
            "BunnyNames_Angora" => BunnyNames_Angora,
            "BunnyNames_Dutch" => BunnyNames_Dutch,
            "BunnyNames_Flemish" => BunnyNames_Flemish,
            "BunnyNames_Lop" => BunnyNames_Lop,
            "BunnyNames_Silver" => BunnyNames_Silver,
            "SlimeNames_Blue" => SlimeNames_Blue,
            "SlimeNames_Green" => SlimeNames_Green,
            "SlimeNames_Old" => SlimeNames_Old,
            "SlimeNames_Purple" => SlimeNames_Purple,
            "SlimeNames_Rainbow" => SlimeNames_Rainbow,
            "SlimeNames_Red" => SlimeNames_Red,
            "SlimeNames_Yellow" => SlimeNames_Yellow,
            "SlimeNames_Copper" => SlimeNames_Copper,
            _ => Array.Empty<string>()
        };
        return values.Length == 0 ? string.Empty : values[random.Next(values.Length)];
    }

    private static readonly string[] MerchantNames =
    [
        "Alfred", "Barney", "Calvin", "Edmund", "Edwin", "Eugene",
        "Frank", "Frederick", "Gilbert", "Gus", "Wilbur", "Seymour",
        "Louis", "Humphrey", "Harold", "Milton", "Mortimer", "Howard",
        "Walter", "Isaac", "Joseph", "Kristian", "Ralph",
    ];

    private static readonly string[] NurseNames =
    [
        "Molly", "Amy", "Claire", "Emily", "Katie", "Madeline",
        "Katelyn", "Emma", "Abigail", "Carly", "Jenna", "Heather",
        "Katherine", "Caitlin", "Kaitlin", "Holly", "Kaitlyn", "Hannah",
        "Kathryn", "Lorraine", "Helen", "Kayla", "Lisa", "Allison",
    ];

    private static readonly string[] ArmsDealerNames =
    [
        "DeShawn", "DeAndre", "Marquis", "Darnell", "Terrell", "Malik",
        "Trevon", "Tyrone", "Willie", "Dominique", "Demetrius", "Reginald",
        "Jamal", "Maurice", "Jalen", "Darius", "Xavier", "Terrance",
        "Andre", "Dante", "Brimst", "Bronson", "Tony", "Darryl",
    ];

    private static readonly string[] DryadNames =
    [
        "Alalia", "Alura", "Ariella", "Caelia", "Calista", "Chryseis",
        "Emerenta", "Elysia", "Evvie", "Faye", "Felicitae", "Lunette",
        "Nata", "Nissa", "Tatiana", "Rosalva", "Shea", "Tania",
        "Isis", "Celestia", "Xylia",
    ];

    private static readonly string[] GuideNames =
    [
        "Joe", "Connor", "Tanner", "Wyatt", "Cody", "Levi",
        "Luke", "Jack", "Scott", "Logan", "Cole", "Asher",
        "Bradley", "Jacob", "Garrett", "Dylan", "Maxwell", "Steve",
        "Brett", "Andrew", "Harley", "Kyle", "Jake", "Ryan",
        "Jeffrey", "Seth", "Marty", "Brandon", "Zach", "Jeff",
        "Daniel", "Trent", "Kevin", "Brian", "Colin", "Jan",
    ];

    private static readonly string[] DemolitionistNames =
    [
        "Dolbere", "Bazdin", "Durim", "Tordak", "Garval", "Morthal",
        "Oten", "Dolgen", "Gimli", "Gimut", "Duerthen", "Beldin",
        "Jarut", "Ovbere", "Norkas", "Dolgrim", "Boften", "Norsun",
        "Dias", "Fikod", "Urist", "Darur",
    ];

    private static readonly string[] ClothierNames =
    [
        "Sebastian", "Rupert", "Clive", "Nigel", "Mervyn", "Cedric",
        "Pip", "Cyril", "Fitz", "Lloyd", "Arthur", "Rodney",
        "Graham", "Edward", "Alfred", "Edmund", "Henry", "Herald",
        "Roland", "Lincoln", "Edgar", "Eustace", "Benjamin", "James",
        "Rodrick",
    ];

    private static readonly string[] GoblinTinkererNames =
    [
        "Grodax", "Sarx", "Xon", "Mrunok", "Nuxatk", "Tgerd",
        "Darz", "Smador", "Stazen", "Mobart", "Knogs", "Tkanus",
        "Negurk", "Nort", "Durnok", "Trogem", "Stezom", "Gnudar",
        "Ragz", "Fahd", "Xanos", "Arback", "Fjell", "Dalek",
        "Knub",
    ];

    private static readonly string[] WizardNames =
    [
        "Dalamar", "Dulais", "Elric", "Arddun", "Maelor", "Leomund",
        "Hirael", "Gwentor", "Greum", "Gearroid", "Fizban", "Ningauble",
        "Seonag", "Sargon", "Merlyn", "Magius", "Berwyn", "Arwyn",
        "Alasdair", "Tagar", "Abram", "Xanadu",
    ];

    private static readonly string[] MechanicNames =
    [
        "Shayna", "Korrie", "Ginger", "Brooke", "Jenny", "Autumn",
        "Nancy", "Ella", "Kayla", "Selah", "Sophia", "Marshanna",
        "Lauren", "Trisha", "Shirlena", "Sheena", "Ellen", "Amy",
        "Dawn", "Susana", "Meredith", "Selene", "Terra", "Sally",
    ];

    private static readonly string[] TruffleNames =
    [
        "Reishi", "Maitake", "Chanterelle", "Porcini", "Shimeji", "Amanita",
        "Muscaria", "Agaric", "Cremini", "Morel", "Enoki", "Shiitake",
        "Leinspores",
    ];

    private static readonly string[] SteampunkerNames =
    [
        "Whitney", "Verity", "Ada", "Cornelia", "Lydia", "Leila",
        "Minerva", "Emeline", "Cynthia", "Fidelia", "Lilly", "Phoebe",
        "Zylphia", "Zelda", "Selina", "Hope", "Isabella", "Judith",
        "Savannah", "Vivian",
    ];

    private static readonly string[] DyeTraderNames =
    [
        "Abdosir", "Akbar", "Bodashtart", "Danel", "Hanno", "Hiram",
        "Kanmi", "Philosir", "Tabnit", "Yutpan", "Ahirom", "Batnoam",
        "Sikarbaal", "Hannibal", "Yehomilk", "Ahinadab",
    ];

    private static readonly string[] PartyGirlNames =
    [
        "Candy", "Isis", "Trixy", "Destiny", "Lexus", "Bambi",
        "Bailey", "Glitter", "Sparkle", "Paris", "Dazzle", "Fantasy",
        "Bunny", "Sugar", "Fantasia", "Star", "Cherry", "Amanda",
    ];

    private static readonly string[] CyborgNames =
    [
        "Alpha", "Beta", "Delta", "Omega", "Gamma", "Theta",
        "Kappa", "Omicron", "Sigma", "Upsilon", "Phi", "Zeta",
        "Lambda", "Nu", "Ci", "Rho", "Fender", "T-3E0",
        "Niner-7", "A.N.D.Y.", "Syn-X", "Mu", "Raynbro",
    ];

    private static readonly string[] PainterNames =
    [
        "Marco", "Guido", "Enzo", "Martino", "Mauro", "Lorenzo",
        "Ludo", "Luciano", "Carlo", "Bruno", "Mario", "Leonardo",
        "Raphael", "Luigi", "Luca", "Darren", "Esreadel", "Stefano",
        "Jim",
    ];

    private static readonly string[] WitchDoctorNames =
    [
        "Abibe", "Jamundi", "U'wa", "Tairona", "Xirigua", "Zop'a",
        "Opuni", "Kogi-ghi", "Konah", "Gboto",
    ];

    private static readonly string[] PirateNames =
    [
        "David", "Red Beard", "Jack", "Black Beard", "Captain Morgan", "Wet Beard",
        "James T. Beard", "Gunpowder Garry", "Captain Stoney Dirt", "Jake", "Captain Bullywort",
    ];

    private static readonly string[] StylistNames =
    [
        "Bri", "Brianne", "Flora", "Iris", "Scarlett", "Lola",
        "Hazel", "Stella", "Pearl", "Tallulah", "Ruby", "Esmeralda",
        "Kylie", "Kati", "Biah", "Meliyah", "Petra", "Rox",
        "Roxanne", "Annabel",
    ];

    private static readonly string[] TravelingMerchantNames =
    [
        "Abraham", "Bohemas", "Eladon", "Aphraim", "Gallius", "Llewellyn",
        "Riley", "Rawleigh", "Shipton", "Romeo", "Aedan", "Mercer",
        "Willy",
    ];

    private static readonly string[] AnglerNames =
    [
        "Bobby", "Sammy", "Jimmy", "Danny", "Timmy", "Simon",
        "Johnny", "Billy", "Matty", "Bart", "Phillip", "Spencer",
        "Nathan", "Grayson", "Miles", "Charles", "Adam", "Tyler",
        "Jey", "Ivan", "Izzy",
    ];

    private static readonly string[] SkeletonMerchantNames =
    [
        "Skellington", "Bones McGee", "Gloomy Mays", "Jack Sellington", "Billy Marrows", "Tom",
        "Rattles Magoo", "Mandible Calavera", "Mika", "No-Eyed Wiley", "Victor",
    ];

    private static readonly string[] TaxCollectorNames =
    [
        "McKinly", "Millard", "Fillmore", "Rutherford", "Chester", "Grover",
        "Cleveland", "Theodore", "Herbert", "Warren", "Lyndon", "Ronald",
        "Harrison", "Woodrow", "Tweed", "Blanton", "Dwyer", "Carroll",
        "Agnew",
    ];

    private static readonly string[] BartenderNames =
    [
        "Ted", "Barkeep", "Jerry", "Bill", "Ernest", "William",
        "Dale", "Bruce", "Moe", "Javahawk", "Elandrian", "Driscan",
        "Iamisom", "Blacksmith", "Dani Moo", "Paddy",
    ];

    private static readonly string[] GolferNames =
    [
        "Lion Pines", "Gunner McLovin", "Harry Madison", "Arnie Palmfrond", "Jake Nicklelisp", "William",
        "Billy Bones", "Jim Mulligan",
    ];

    private static readonly string[] BestiaryGirlNames =
    [
        "Maria", "MacKenzie", "Azaria", "Juanita", "Astra", "Hayley",
        "Becca", "Veronica", "Tia", "Robyn", "Arien", "Jane",
        "Bindi", "Dian", "Lori", "Mardy", "Mollie", "Lizzy",
        "Daphne", "Ellen", "Lilith",
    ];

    private static readonly string[] PrincessNames =
    [
        "Ai", "Hime", "Emilia", "Alice", "Belle", "Sophia",
        "Hikari", "Faye", "Christina", "Flora", "Liselotte", "Aria",
        "Yuuki", "Theresa", "Elitia", "Lucia", "Ariel", "Momo",
        "Charlotte", "Tiara", "Yorai", "Rosetta",
    ];

    private static readonly string[] CatNames_Siamese =
    [
        "Zen", "Callie", "Rainha", "Nust", "Bandit", "Goliath",
        "Valerie", "Socks", "Iris", "Sekhmet", "Cleo", "Sappho",
    ];
    private static readonly string[] CatNames_Black =
    [
        "Luna", "Figment", "Ember", "Damien", "Snowball", "Night",
        "Misu", "Magic", "Tonic", "Violet", "Jaspers", "Gargoyle",
        "Lilith", "Salem", "Rascal", "Bella", "Odin", "Jiji",
        "Morgan", "Arbus", "Max", "George", "Esper",
    ];
    private static readonly string[] CatNames_OrangeTabby =
    [
        "Linkle", "Remy", "Lux", "Sunny", "Tigger", "Callie",
        "Sassy", "Bob", "Leo", "Tiger", "Caramel", "Tony",
        "Garfield", "Hobbes", "Charlie", "Rascal", "Critter", "Adol",
        "Lawnmower",
    ];
    private static readonly string[] CatNames_RussianBlue =
    [
        "Alexi", "Ruby", "Bubbles", "Lexi", "Prissy", "Valery",
        "Lucy", "Pete", "Jocelyn", "Light", "Lily", "Starlight",
        "Cobalt", "Tilly", "Rion", "Momo", "Tarzan",
    ];
    private static readonly string[] CatNames_Silver =
    [
        "Roo", "Blaze", "Rebus", "Polo", "Kelly", "Kiva",
        "Sooty", "By-Tor", "Merlin", "Trudy", "Nana", "Ange",
        "Kanako", "Freya", "Cloud", "Cutie", "Renne", "Miette",
        "Tula", "Kibby",
    ];
    private static readonly string[] CatNames_White =
    [
        "Misty", "Storm", "Angel", "Vincent", "Mr. Cat", "Holly",
        "Sam", "Rory", "Ichibi", "Belle", "Yuki", "Ater",
        "Snowdrop", "Snowball", "Reah", "Gemma",
    ];

    private static readonly string[] DogNames_Labrador =
    [
        "Penny", "Pebbles", "Puppy", "Monroe", "Kittie", "Buddy",
        "Charlie", "Trulte", "Daisy", "Calvin", "Willow", "Arthur",
        "John", "Bitsy", "Mike", "Sparky", "Lucky", "Bubbles",
        "Tank",
    ];
    private static readonly string[] DogNames_PitBull =
    [
        "Max", "Dexter", "Chomps", "Bubba", "Sampson", "Spike",
        "Ginger", "Kona", "Keith", "Jazzy", "Rex", "Toshiro",
        "Buster", "Doug",
    ];
    private static readonly string[] DogNames_Beagle =
    [
        "Zelda", "Navi", "Ali", "Fred", "Roxie", "Milo",
        "Copper", "Darwin", "Lieselotte", "Wilma", "Odie", "Feena",
        "Pete",
    ];
    private static readonly string[] DogNames_Corgi =
    [
        "Bella", "Ellie", "Baxter", "Oliver", "Maya", "Titus",
        "Shino", "Elizabeth", "Victoria", "Yona", "Ammy", "Charles",
        "Estelle", "Astro", "Kilo", "AK",
    ];
    private static readonly string[] DogNames_Dalmation =
    [
        "Spot", "Suzy", "Lucky", "Marshall", "Dotty", "Pongo",
        "Sapphire", "Splat", "Aurora", "Akane", "Firebug", "Polka",
        "Mizore",
    ];
    private static readonly string[] DogNames_Husky =
    [
        "Rosalie", "Balto", "Miska", "Sky", "Sasha", "Zoey",
        "Toya", "Snowdog", "Kayla", "Hachiko", "Indiana", "Sven",
        "Bullet", "Comet", "Ingrid", "Dogi",
    ];

    private static readonly string[] BunnyNames_White =
    [
        "Bunnicula", "Ben", "Roger", "Skippy", "Donnie", "Alice",
        "Presto", "Shion", "Lisette", "Lily", "Inaba", "Shirayuki",
        "Lilia", "Ears", "Jimothy",
    ];
    private static readonly string[] BunnyNames_Angora =
    [
        "Breadbuns", "Loaf", "Babs", "Big B", "Greg", "Fluffy",
        "Pom", "Muffin", "Maximus", "Donald",
    ];
    private static readonly string[] BunnyNames_Dutch =
    [
        "Oreo", "Olaf", "Domino", "Oz", "Yin", "Yang",
        "Pow", "Kaguya", "Mono", "Panda", "Orin",
    ];
    private static readonly string[] BunnyNames_Flemish =
    [
        "Thumper", "Cinnabun", "Peter", "Hazel", "Loki", "Sunflower",
        "Toby", "Daisy", "Dusty", "Jazz", "Russell", "Honeycomb",
    ];
    private static readonly string[] BunnyNames_Lop =
    [
        "Bunbun", "Lola", "Max", "Tyler", "Flops", "Flappie",
        "Caerbannog", "Mochi", "Dandy", "Puff", "Youp", "Boof",
        "Veidra",
    ];
    private static readonly string[] BunnyNames_Silver =
    [
        "Bugz", "Buster", "Silvine", "Bunstar", "Lord", "Bunnykins",
        "Crystal", "Chrome", "Vincent", "Zach", "Joshua", "Dustbunny",
        "Dusty",
    ];

    private static readonly string[] SlimeNames_Blue =
    [
        "Steve", "Seymour", "Simon", "Sheldon", "Sam", "Samson",
        "Sid", "Sigvard", "Stein", "Sean", "Samwise", "Snoot",
        "Swotty", "Stuart", "Stratford", "Saliva", "Sherman", "Stiltskin",
        "Shibboleet", "Sine", "Spock", "Smarty", "Sonic", "Sputnik",
        "Smoof", "Spetch", "Stevenson",
    ];
    private static readonly string[] SlimeNames_Green =
    [
        "Sylvester", "Slimer", "Seth", "Shane", "Sully", "Scooter",
        "Scotty", "Spike", "Slachi", "Spinach", "Slick", "Slippy",
        "Speedy", "Slurm", "School", "Swag", "Swiggity Swooty", "Slackin",
        "Switch", "Sport", "Squash", "Skeeter", "Scutiger", "Softball",
        "Spazzy",
    ];
    private static readonly string[] SlimeNames_Old =
    [
        "Stooge", "Sir Slushington III", "Santiago", "Sebastian", "Samuel", "Seamus",
        "Sage", "Sinclair", "Stanford", "Septimus", "Stinky", "Spiffy",
        "Spruce", "Swanky", "Soggy", "Scooby", "Sigmund", "Smithers",
        "Sassafras", "Slumbledore", "Slimdalf", "Schartzmugel", "Scion", "Scrooge",
        "Stonks", "Swanson", "Spinkerton",
    ];
    private static readonly string[] SlimeNames_Purple =
    [
        "Stewy", "Sue", "Spencer", "Sidney", "Stefan", "Sonnie",
        "Sacco", "Sunset", "Snafu", "Stumble", "Spaghetti", "Shoggy",
        "Slimefors", "Shirley", "Sara", "Sharon", "Sludge", "Snuggle",
        "Sloppy", "Slimeora", "Sylph", "Spew", "Shemp", "Spanky",
        "Spork",
    ];
    private static readonly string[] SlimeNames_Rainbow =
    [
        "Sylvia", "Skye", "Stephanie", "Sophia", "Shanna", "Serenity",
        "Shian", "Slimestar", "Shimmer", "Surai", "Stella", "Sasha",
        "Sweety", "Sparkle", "Sapphire", "Seraph", "Starry", "Sprinkes",
        "Sherbet", "Sugar", "Stardew", "Sheena", "Sveta", "Shampoo",
        "Sproink", "Secreta", "Sponsa", "Skyler", "Sorai",
    ];
    private static readonly string[] SlimeNames_Red =
    [
        "Sparky", "Sourpuss", "Steamer", "Sizzle", "Shaka", "Slippery Sal",
        "Sirius", "Sven", "Sly", "Smeagol", "Scuff", "Shifty",
        "Sassy", "Scorpius", "Snotty", "Sleepy", "Stressball", "Slurpee",
        "Squeezo", "Sapo", "Snart", "Skiprat", "Sully", "Sharkey",
        "Scarface", "Slickfather", "Slimeballa",
    ];
    private static readonly string[] SlimeNames_Yellow =
    [
        "Sandy", "Saoirse", "Selena", "Sienna", "Samara", "Simone",
        "Siobhan", "Sabrina", "Sunny", "Savannah", "Spirit", "Sinead",
        "Saraid", "Siofra", "Sorcha", "Salem", "Soleil", "Salvo",
        "Sahara", "Shanoa", "Sybil", "Salome", "Summer", "Swirl",
        "Saga", "Silva", "Striga",
    ];
    private static readonly string[] SlimeNames_Copper =
    [
        "Solomon", "Shortsword Saxon", "Steele", "Sigfried", "Sterling", "Seniore Slash",
        "Stabby", "Spunky", "Sharpie", "Scout", "Scrappy", "Slade",
        "Samsara", "Six-Swing Serge", "Scorch", "Sabin", "Stryker", "Slayer",
        "Slival", "Sting", "Sabaton", "Semper Serus", "Scrimshank", "Scavager",
        "Selmy", "Shortie",
    ];
}

/// <summary>
/// Source-shaped town identity generation for NPC.GiveTownUniqueDataToNPCsThatNeedIt. It deliberately owns two RNG
/// streams because vanilla uses WorldGen.genRand for name categories and Main.rand for VariantNPCProfile selection.
/// Legacy/Transformable profiles consume a second name roll after the initial getNewNPCName assignment.
/// </summary>
public sealed class VanillaTownNpcIdentityResolver1458
{
    private static readonly string[] CatVariants = ["Siamese", "Black", "OrangeTabby", "RussianBlue", "Silver", "White"];
    private static readonly string[] DogVariants = ["Labrador", "PitBull", "Beagle", "Corgi", "Dalmation", "Husky"];
    private static readonly string[] BunnyVariants = ["White", "Angora", "Dutch", "Flemish", "Lop", "Silver"];

    private readonly IVanillaTownNpcNameCatalog1458 names;
    private readonly IVanillaTownNpcRandom1458 mainRandom;
    private readonly IVanillaTownNpcRandom1458 worldGenRandom;

    public VanillaTownNpcIdentityResolver1458(
        IVanillaTownNpcNameCatalog1458? names = null,
        IVanillaTownNpcRandom1458? mainRandom = null,
        IVanillaTownNpcRandom1458? worldGenRandom = null)
    {
        this.names = names ?? VanillaEnglishTownNpcNameCatalog1458.Instance;
        this.mainRandom = mainRandom ?? new SystemVanillaTownNpcRandom1458();
        this.worldGenRandom = worldGenRandom ?? new SystemVanillaTownNpcRandom1458();
    }

    public VanillaTownNpcSpawnIdentity1458 Resolve(NpcTypeId type, bool shimmeredTownNpc)
    {
        string category = GetBaseNameCategory(type);
        string givenName = RollName(category);
        int variationIndex = 0;

        string[]? variants = type.Value switch
        {
            637 => CatVariants,
            638 => DogVariants,
            656 => BunnyVariants,
            _ => null
        };
        if (variants is not null)
        {
            variationIndex = mainRandom.Next(variants.Length);
            string prefix = type.Value switch
            {
                637 => "CatNames_",
                638 => "DogNames_",
                656 => "BunnyNames_",
                _ => throw new InvalidOperationException()
            };
            givenName = RollName(prefix + variants[variationIndex]);
        }
        else if (HasTownNpcProfile1458(type))
        {
            givenName = RollName(category);
        }

        if (shimmeredTownNpc)
            variationIndex = 1;
        return new VanillaTownNpcSpawnIdentity1458(givenName, variationIndex);
    }

    private string RollName(string category) => category.Length == 0
        ? string.Empty
        : names.RandomFromCategory(category, worldGenRandom);

    private static string GetBaseNameCategory(NpcTypeId type) => type.Value switch
    {
        17 => "MerchantNames",
        18 => "NurseNames",
        19 => "ArmsDealerNames",
        20 => "DryadNames",
        22 => "GuideNames",
        38 => "DemolitionistNames",
        54 => "ClothierNames",
        107 => "GoblinTinkererNames",
        108 => "WizardNames",
        124 => "MechanicNames",
        160 => "TruffleNames",
        178 => "SteampunkerNames",
        207 => "DyeTraderNames",
        208 => "PartyGirlNames",
        209 => "CyborgNames",
        227 => "PainterNames",
        228 => "WitchDoctorNames",
        229 => "PirateNames",
        353 => "StylistNames",
        368 => "TravelingMerchantNames",
        369 => "AnglerNames",
        441 => "TaxCollectorNames",
        453 => "SkeletonMerchantNames",
        550 => "BartenderNames",
        588 => "GolferNames",
        633 => "BestiaryGirlNames",
        663 => "PrincessNames",
        637 => "CatNames_Siamese",
        638 => "DogNames_Labrador",
        656 => "BunnyNames_White",
        670 => "SlimeNames_Blue",
        678 => "SlimeNames_Green",
        679 => "SlimeNames_Old",
        680 => "SlimeNames_Purple",
        681 => "SlimeNames_Rainbow",
        682 => "SlimeNames_Red",
        683 => "SlimeNames_Yellow",
        684 => "SlimeNames_Copper",
        _ => string.Empty
    };

    private static bool HasTownNpcProfile1458(NpcTypeId type) => type.Value is
        17 or 18 or 19 or 20 or 22 or 37 or 38 or 54 or 107 or 108 or 124 or 142 or 160 or 178 or 207 or 208 or
        209 or 227 or 228 or 229 or 353 or 368 or 369 or 441 or 453 or 550 or 588 or 633 or 663 or 637 or 638 or
        656 or 670 or 678 or 679 or 680 or 681 or 682 or 683 or 684;
}
