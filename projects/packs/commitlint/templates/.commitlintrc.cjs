module.exports = {
  extends: ['@commitlint/config-conventional'],
  rules: {
    'header-max-length': [1, 'always', 144],
    'subject-case': [2, 'always', ['sentence-case', 'start-case']],
    'type-case': [2, 'always', ['lower-case']],
    'type-enum': [
      2,
      'always',
      ['build', 'ci', 'docs', 'feat', 'fix', 'revert', 'release']
    ],
    'scope-case': [2, 'always', ['lower-case']],
    'scope-enum': [
      2,
      'always',
      {
        scopes: [
          null{{ if commitlintScopes contains "api" }},
          'api'{{ end }}{{ if commitlintScopes contains "backend" }},
          'backend'{{ end }}{{ if commitlintScopes contains "build" }},
          'build'{{ end }}{{ if commitlintScopes contains "cli" }},
          'cli'{{ end }}{{ if commitlintScopes contains "docs" }},
          'docs'{{ end }}{{ if commitlintScopes contains "frontend" }},
          'frontend'{{ end }}{{ if commitlintScopes contains "infrastructure" }},
          'infrastructure'{{ end }}{{ if commitlintScopes contains "packs" }},
          'packs'{{ end }}{{ if commitlintScopes contains "schema" }},
          'schema'{{ end }}{{ if commitlintScopes contains "security" }},
          'security'{{ end }}{{ if commitlintScopes contains "tests" }},
          'tests'{{ end }}{{ if commitlintScopes contains "website" }},
          'website'{{ end }}
        ],
        delimiters: [',']
      }
    ]
  }
}
