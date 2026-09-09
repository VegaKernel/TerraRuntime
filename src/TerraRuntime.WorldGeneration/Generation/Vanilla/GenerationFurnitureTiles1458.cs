namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Main's initialized tileTable/tileLighted arrays, verified against TerrariaServer 1.4.5.8.</summary>
internal static class GenerationFurnitureTiles1458
{
    internal static bool IsTable(ushort type) => type is
        14 or 18 or 19 or 87 or 88 or 101 or 114 or 275 or 276 or 277 or 278 or 279 or 280 or 281 or
        285 or 286 or 296 or 297 or 298 or 299 or 309 or 310 or 339 or 358 or 359 or 361 or 362 or 363 or
        364 or 376 or 380 or 391 or 392 or 393 or 394 or 405 or 413 or 414 or 427 or 435 or 436 or 437 or
        438 or 439 or 469 or 532 or 533 or 538 or 542 or 544 or 550 or 551 or 553 or 554 or 555 or 556 or
        558 or 559 or 582 or 599 or 600 or 601 or 602 or 603 or 604 or 605 or 606 or 607 or 608 or 609 or
        610 or 611 or 612 or 619 or 629 or 632 or 640 or 643 or 644 or 645 or 710;
    internal static bool IsLighted(ushort type) => type is
        4 or 17 or 19 or 20 or 22 or 26 or 27 or 31 or 33 or 34 or 35 or 37 or 42 or 49 or
        58 or 61 or 70 or 71 or 72 or 76 or 77 or 83 or 84 or 92 or 93 or 95 or 96 or 98 or
        100 or 109 or 125 or 126 or 129 or 133 or 140 or 149 or 160 or 171 or 173 or 174 or 184 or 190 or
        204 or 209 or 215 or 237 or 238 or 262 or 263 or 264 or 265 or 266 or 267 or 268 or 270 or 271 or
        286 or 302 or 316 or 317 or 318 or 327 or 336 or 340 or 341 or 342 or 343 or 344 or 346 or 347 or
        348 or 349 or 350 or 354 or 356 or 370 or 372 or 381 or 390 or 391 or 405 or 415 or 416 or 417 or
        418 or 429 or 463 or 491 or 500 or 501 or 502 or 503 or 517 or 519 or 528 or 534 or 535 or 536 or
        537 or 539 or 540 or 548 or 564 or 568 or 569 or 570 or 572 or 578 or 580 or 581 or 582 or 592 or
        593 or 594 or 597 or 598 or 613 or 614 or 619 or 620 or 625 or 626 or 627 or 628 or 633 or 634 or
        637 or 638 or 646 or 656 or 658 or 659 or 660 or 663 or 667 or 684 or 687 or 688 or 689 or 690 or
        691 or 692 or 695 or 696 or 699 or 701 or 703 or 708 or 711 or 717 or 718 or 719 or 739;
}
