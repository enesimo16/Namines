import type { MetadataRoute } from 'next';

export default function sitemap(): MetadataRoute.Sitemap {
  const baseUrl = (process.env.NEXT_PUBLIC_APP_URL || 'https://namines.app').replace(/\/+$/, '');
  const now = new Date();

  const routes: {
    path: string;
    priority: number;
    changeFrequency: MetadataRoute.Sitemap[number]['changeFrequency'];
    includeAlternates?: boolean;
  }[] = [
    { path: '', priority: 1.0, changeFrequency: 'daily', includeAlternates: true },
    { path: '/en', priority: 1.0, changeFrequency: 'daily', includeAlternates: true },
    { path: '/tr', priority: 1.0, changeFrequency: 'daily', includeAlternates: true },
    { path: '/new', priority: 0.9, changeFrequency: 'weekly' },
    { path: '/security', priority: 0.6, changeFrequency: 'monthly' },
    { path: '/terms', priority: 0.4, changeFrequency: 'monthly' },
    { path: '/privacy', priority: 0.4, changeFrequency: 'monthly' },
  ];

  return routes.map((route) => {
    const entry: MetadataRoute.Sitemap[number] = {
      url: `${baseUrl}${route.path}`,
      lastModified: now,
      changeFrequency: route.changeFrequency,
      priority: route.priority,
    };

    if (route.includeAlternates) {
      entry.alternates = {
        languages: {
          en: `${baseUrl}/en`,
          tr: `${baseUrl}/tr`,
          'x-default': `${baseUrl}/`,
        },
      };
    }

    return entry;
  });
}
