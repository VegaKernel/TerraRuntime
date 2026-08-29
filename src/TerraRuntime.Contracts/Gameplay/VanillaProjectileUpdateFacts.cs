namespace TerraRuntime.Contracts.Gameplay;

/// <summary>
/// Version-pinned TerrariaServer 1.4.5.8 Projectile.extraUpdates defaults. Vanilla executes one ordinary
/// projectile Update body plus <c>extraUpdates</c> additional subupdates per world tick, and timeLeft is
/// decremented once inside each subupdate. Jester's Arrow (type 5) and Bullet (type 14) are the currently
/// simulated world-flight projectiles in this catalog with extraUpdates=1, so one world tick executes two
/// authoritative subupdates for either type.
/// The source assembly SHA-256 is d87e3faf08637f6be8882c63e7f11fb7e792b0230006309618473ece0f863e1e.
/// </summary>
public static class VanillaProjectileUpdateFacts
{
    public const int ExtraUpdateTypeCount = 234;
    public const int MaximumExtraUpdates = 180;

    public static int GetExtraUpdates(ProjectileTypeId type)
    {
        if (!VanillaProjectileIds.IsLiveWireType(type))
            return 0;

        return type.Value switch
        {
            5 or 14 or 36 or 42 or 65 or 68 or 89 or 110 or 120 or 158 or 159 or 160 or 161 or 180 or 182 or 195 or 225 or 239 or 245 or 246 or 256 or 257 or 259 or 261 or 262 or 264 or 278 or 282 or 293 or 297 or 306 or 336 or 337 or 348 or 354 or 376 or 444 or 449 or 459 or 467 or 468 or 477 or 484 or 503 or 521 or 532 or 538 or 556 or 557 or 558 or 559 or 560 or 561 or 582 or 593 or 631 or 636 or 639 or 660 or 661 or 709 or 710 or 711 or 712 or 731 or 763 or 772 or 802 or 819 or 841 or 842 or 848 or 857 or 866 or 902 or 912 or 913 or 914 or 931 or 933 or 938 or 939 or 940 or 941 or 942 or 943 or 944 or 945 or 952 or 964 or 981 or 1021 or 1024 or 1028 or 1029 or 1030 or 1031 or 1032 or 1033 or 1034 or 1035 or 1037 or 1039 or 1045 or 1049 or 1079 or 1097 or 1100 or 1104 or 1120 or 1124 or 1127 => 1,
            20 or 22 or 83 or 84 or 85 or 100 or 104 or 114 or 117 or 145 or 146 or 147 or 148 or 149 or 188 or 207 or 221 or 279 or 280 or 283 or 284 or 285 or 286 or 287 or 288 or 299 or 301 or 357 or 358 or 389 or 406 or 409 or 424 or 425 or 426 or 437 or 440 or 496 or 576 or 577 or 594 or 606 or 616 or 622 or 629 or 634 or 640 or 715 or 716 or 717 or 718 or 729 or 847 or 849 or 856 or 915 or 916 or 977 or 1015 or 1016 or 1017 or 1026 or 1042 or 1084 or 1106 => 2,
            101 or 181 or 189 or 298 or 307 or 309 or 323 or 356 or 438 or 462 or 566 or 592 or 635 or 818 or 876 or 935 => 3,
            88 or 466 or 580 => 4,
            524 or 638 or 645 => 5,
            242 or 302 => 7,
            305 => 10,
            601 => 30,
            766 or 767 or 768 or 769 or 770 or 771 or 822 or 823 or 824 or 826 or 827 or 828 or 829 or 830 or 838 or 839 or 840 or 843 or 844 or 845 or 846 or 850 or 852 or 853 => 60,
            255 or 260 or 290 or 294 or 433 or 434 => 100,
            227 => 180,
            _ => 0
        };
    }

    public static int GetSubupdatesPerWorldTick(ProjectileTypeId type) =>
        checked(GetExtraUpdates(type) + 1);
}
