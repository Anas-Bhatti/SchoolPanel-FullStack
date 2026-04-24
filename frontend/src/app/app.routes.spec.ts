import { routes } from './app.routes';

describe('App Routes', () => {
  it('should define main route structure', () => {
    expect(routes).toBeTruthy();
    expect(Array.isArray(routes)).toBe(true);

    const authRoute = routes.find(r => r.path === 'auth');
    const shellRoute = routes.find(r => r.path === '');
    const notFoundRoute = routes.find(r => r.path === '404');
    const wildcardRoute = routes.find(r => r.path === '**');

    expect(authRoute).toBeDefined();
    expect(shellRoute).toBeDefined();
    expect(notFoundRoute).toBeDefined();
    expect(wildcardRoute).toBeDefined();

    expect(authRoute?.canActivate).toEqual(jasmine.any(Array));
    expect(shellRoute?.canActivate).toEqual(jasmine.any(Array));
    expect(notFoundRoute?.loadComponent).toEqual(jasmine.any(Function));

    expect(wildcardRoute?.redirectTo).toBe('/404');
    expect(wildcardRoute?.pathMatch).toBe('full');
  });

  it('should contain nested student routes with permission guard', () => {
    const shellRoute = routes.find(r => r.path === '');
    const studentsRoute = shellRoute?.children?.find(r => r.path === 'students');
    expect(studentsRoute).toBeDefined();
    expect(studentsRoute?.data?.permission).toBe('Students');
    expect(studentsRoute?.canActivate).toEqual(expect.any(Array));

    const studentChildPaths = studentsRoute?.children?.map(c => c.path);
    expect(studentChildPaths).toEqual(expect.arrayContaining(['', 'new', ':id/edit', ':id']));
  });
});