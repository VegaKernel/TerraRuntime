namespace TerraRuntime.Contracts.Gameplay;

/// <summary>Initial lifecycle fields assigned by TerrariaServer 1.4.5.8 Projectile.SetDefaults.</summary>
public readonly record struct VanillaProjectileLifecycleDefaults(int TimeLeft, bool NetImportant);

/// <summary>
/// Version-pinned lifecycle facts extracted from official TerrariaServer 1.4.5.8 Projectile.SetDefaults.
/// The source assembly SHA-256 is d87e3faf08637f6be8882c63e7f11fb7e792b0230006309618473ece0f863e1e.
/// </summary>
public static class VanillaProjectileLifecycleFacts
{
    public const int DefaultTimeLeft = 3600;
    public const int UndefinedTypeCount = 5;
    public const int NetImportantTypeCount = 295;
    public const int TimeLeftOverrideCount = 458;

    private static readonly ushort[] UndefinedTypes =
    [
        457, 458, 832, 924, 925
    ];

    private static readonly ushort[] NetImportantTypes =
    [
        13, 18, 25, 26, 32, 35, 50, 53, 63, 72, 73, 74, 86, 87, 111, 112, 127, 154, 155, 163,
        165, 191, 192, 193, 194, 197, 198, 199, 200, 208, 210, 211, 226, 227, 230, 231, 232, 233, 234, 235,
        236, 237, 238, 243, 244, 247, 256, 266, 268, 269, 308, 310, 313, 314, 315, 317, 319, 322, 324, 331,
        332, 334, 353, 360, 361, 362, 363, 364, 365, 366, 372, 373, 375, 377, 380, 381, 382, 387, 388, 390,
        391, 392, 393, 394, 395, 396, 398, 403, 407, 423, 446, 473, 486, 487, 488, 489, 490, 492, 499, 500,
        515, 533, 602, 613, 623, 625, 626, 627, 628, 641, 643, 646, 647, 648, 649, 650, 652, 653, 663, 665,
        667, 677, 678, 679, 688, 689, 690, 691, 692, 693, 701, 702, 703, 721, 739, 740, 741, 742, 743, 744,
        745, 746, 747, 748, 749, 750, 751, 752, 753, 755, 757, 758, 759, 760, 764, 765, 774, 775, 815, 816,
        817, 820, 821, 822, 823, 824, 825, 826, 827, 828, 829, 830, 831, 833, 834, 835, 838, 839, 840, 843,
        844, 845, 846, 850, 851, 852, 853, 854, 855, 858, 859, 860, 861, 864, 865, 870, 875, 877, 878, 879,
        881, 882, 883, 884, 885, 886, 887, 888, 889, 890, 891, 892, 893, 894, 895, 896, 897, 898, 899, 900,
        901, 928, 934, 935, 946, 947, 948, 951, 956, 957, 958, 959, 960, 963, 966, 970, 986, 987, 988, 989,
        990, 991, 992, 993, 994, 995, 998, 1003, 1004, 1008, 1009, 1010, 1011, 1018, 1022, 1025, 1027, 1036, 1046, 1050,
        1056, 1058, 1059, 1060, 1061, 1062, 1063, 1064, 1065, 1066, 1067, 1068, 1069, 1070, 1071, 1072, 1074, 1075, 1076, 1078,
        1089, 1090, 1091, 1093, 1094, 1095, 1096, 1098, 1101, 1102, 1112, 1113, 1118, 1119, 1123
    ];

    /// <summary>Returns true only when SetDefaults leaves a nonzero protocol-range type active.</summary>
    public static bool IsDefinedLiveType(ProjectileTypeId type) => IsDefinedLiveTypeValue(type.Value);

    internal static bool IsDefinedLiveTypeValue(int rawType)
    {
        if (rawType <= 0 || rawType >= VanillaProjectileIds.Count)
            return false;

        return Array.BinarySearch(UndefinedTypes, checked((ushort)rawType)) < 0;
    }

    public static bool TryGetDefaults(
        ProjectileTypeId type,
        out VanillaProjectileLifecycleDefaults defaults)
    {
        if (!IsDefinedLiveType(type))
        {
            defaults = default;
            return false;
        }

        ushort rawType = checked((ushort)type.Value);
        int timeLeft = rawType switch
        {
            1122 => 1,
            108 or 164 or 178 or 289 or 1002 or 1121 or 1131 or 1132 => 2,
            476 or 608 => 3,
            698 or 1129 => 5,
            880 or 929 => 18,
            682 or 729 or 974 or 977 or 978 => 30,
            265 => 37,
            661 => 40,
            167 or 168 or 169 or 170 or 415 or 416 or 417 or 418 => 45,
            1045 => 48,
            355 => 58,
            187 or 378 or 472 or 612 or 624 or 831 or 864 or 953 or 970 or 976 or 1044 => 60,
            1120 => 75,
            639 or 640 or 731 or 973 or 985 => 90,
            255 or 290 or 376 or 410 or 433 => 100,
            5 or 221 or 264 or 343 or 405 or 526 or 538 or 654 or 670 or 683 or 922 or 932 or 1038 or 1116 => 120,
            76 or 77 or 78 or 114 or 180 or 183 or 227 or 263 or 274 or 480 or 498 or 596 or 923 or 950 or 1023 => 180,
            260 or 873 => 200,
            874 => 210,
            962 => 220,
            344 or 447 or 537 or 659 or 871 or 919 or 931 or 1039 => 240,
            928 => 250,
            616 => 270,
            121 or 122 or 123 or 124 or 125 or 126 or 186 or 239 or 245 or 258 or 294 or 302 or 311 or 312 or 346 or 350 or 379 or 385 or 409 or 478 or 540 or 597 or 706 or 710 or 851 or 965 or 1054 or 1084 => 300,
            206 or 326 or 327 or 328 or 400 or 401 or 402 or 450 or 938 or 939 or 940 or 941 or 942 or 943 or 944 or 945 or 1001 or 1107 or 1108 or 1109 => 360,
            171 or 475 or 505 or 506 => 400,
            325 or 329 or 575 or 618 or 1092 => 420,
            837 => 480,
            384 => 540,
            14 or 20 or 36 or 83 or 84 or 88 or 89 or 90 or 94 or 104 or 110 or 158 or 159 or 160 or 161 or 181 or 189 or 207 or 242 or 257 or 279 or 281 or 283 or 284 or 285 or 286 or 287 or 307 or 316 or 357 or 389 or 440 or 442 or 449 or 452 or 454 or 455 or 456 or 466 or 490 or 539 or 573 or 574 or 576 or 577 or 580 or 606 or 607 or 638 or 660 or 704 or 712 or 981 or 1099 or 1114 or 1124 or 1130 or 1134 or 1135 => 600,
            566 or 872 => 660,
            386 => 840,
            185 or 254 or 348 or 349 or 590 or 644 or 658 or 672 or 673 or 674 or 713 or 1055 => 900,
            855 => 1000,
            715 or 716 or 717 or 718 => 1080,
            1 or 2 or 4 or 91 or 103 or 117 or 120 or 172 or 225 or 278 or 282 or 352 or 469 or 474 or 485 or 495 or 631 or 656 or 657 or 1006 or 1049 => 1200,
            27 => 1800,
            100 => 2700,
            24 => 4800,
            473 => 7200,
            525 or 734 or 1021 => 10800,
            18 or 50 or 53 or 72 or 86 or 87 or 111 or 112 or 127 or 175 or 191 or 192 or 193 or 194 or 197 or 198 or 199 or 200 or 208 or 209 or 210 or 211 or 226 or 236 or 238 or 244 or 266 or 268 or 269 or 313 or 314 or 317 or 319 or 324 or 334 or 353 or 373 or 375 or 380 or 387 or 388 or 390 or 391 or 392 or 393 or 394 or 395 or 398 or 407 or 423 or 482 or 492 or 499 or 500 or 515 or 533 or 613 or 623 or 625 or 626 or 627 or 628 or 650 or 653 or 701 or 702 or 703 or 755 or 758 or 759 or 764 or 765 or 774 or 815 or 816 or 817 or 821 or 825 or 833 or 834 or 835 or 854 or 858 or 859 or 860 or 870 or 875 or 881 or 882 or 883 or 884 or 885 or 886 or 887 or 888 or 889 or 890 or 891 or 892 or 893 or 894 or 895 or 896 or 897 or 898 or 899 or 900 or 901 or 934 or 946 or 951 or 956 or 957 or 958 or 959 or 960 or 963 or 994 or 995 or 998 or 1003 or 1004 or 1018 or 1022 or 1027 or 1046 or 1050 or 1056 or 1089 or 1090 or 1093 or 1094 or 1095 or 1096 or 1112 or 1113 or 1118 or 1119 => 18000,
            13 or 32 or 73 or 74 or 163 or 165 or 230 or 231 or 232 or 233 or 234 or 235 or 256 or 308 or 310 or 315 or 322 or 331 or 332 or 372 or 377 or 396 or 403 or 446 or 486 or 487 or 488 or 489 or 641 or 643 or 646 or 647 or 648 or 649 or 652 or 663 or 665 or 667 or 677 or 678 or 679 or 688 or 689 or 690 or 691 or 692 or 693 or 753 or 865 or 935 or 966 or 1008 or 1009 or 1010 or 1011 or 1025 => 36000,
            820 => 86400,
            _ => DefaultTimeLeft
        };

        defaults = new VanillaProjectileLifecycleDefaults(
            timeLeft,
            Array.BinarySearch(NetImportantTypes, rawType) >= 0);
        return true;
    }

    public static bool IsNetImportant(ProjectileTypeId type) =>
        TryGetDefaults(type, out VanillaProjectileLifecycleDefaults defaults) && defaults.NetImportant;
}
