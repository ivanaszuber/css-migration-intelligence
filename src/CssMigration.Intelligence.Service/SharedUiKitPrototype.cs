// File purpose: Illustrates a single shared module CSS layer that consumes approved configuration values.
namespace CssMigration.Intelligence.Service;

public static class SharedUiKitPrototype
{
    public const string Css = """
        /* Illustrative target UI-kit CSS, not an extracted production migration. */
        /* Layout is shared; tenant values are supplied through approved CSS custom properties. */
        .module-home, .module-learning, .module-operations { box-sizing: border-box; min-width: 0; }
        .module-home { color: var(--color-module-home-foreground, #243027); background: var(--color-module-home-background, #ffffff); font-family: var(--font-module-home-font-family, system-ui); }
        .module-home__hero { color: var(--color-module-home-hero-foreground, #ffffff); background-color: var(--color-module-home-hero-background, #4e7658); border-radius: var(--radius-module-home-hero-corner, 12px); }
        .module-home__card { color: var(--color-module-home-card-foreground, #243027); background: var(--color-module-home-card-background, #ffffff); border-radius: var(--radius-module-home-card-corner, 12px); }
        .module-learning { color: var(--color-module-learning-foreground, #243027); background: var(--color-module-learning-background, #ffffff); font-family: var(--font-module-learning-font-family, system-ui); }
        .module-learning__hero { color: var(--color-module-learning-hero-foreground, #ffffff); background-color: var(--color-module-learning-hero-background, #4e7658); border-radius: var(--radius-module-learning-hero-corner, 12px); }
        .module-learning__card { color: var(--color-module-learning-card-foreground, #243027); background: var(--color-module-learning-card-background, #ffffff); border-radius: var(--radius-module-learning-card-corner, 12px); }
        .module-operations { color: var(--color-module-operations-foreground, #243027); background: var(--color-module-operations-background, #ffffff); font-family: var(--font-module-operations-font-family, system-ui); }
        .module-operations__hero { color: var(--color-module-operations-hero-foreground, #ffffff); background-color: var(--color-module-operations-hero-background, #4e7658); border-radius: var(--radius-module-operations-hero-corner, 12px); }
        .module-operations__card { color: var(--color-module-operations-card-foreground, #243027); background: var(--color-module-operations-card-background, #ffffff); border-radius: var(--radius-module-operations-card-corner, 12px); }
        /* Asset references and layout variants remain separate reviewed configuration fields. */
        """;
}
