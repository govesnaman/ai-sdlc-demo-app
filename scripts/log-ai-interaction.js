#!/usr/bin/env node
'use strict';

const fs = require('node:fs');
const path = require('node:path');

const repositoryRoot = path.resolve(__dirname, '..');
const logPath = path.join(repositoryRoot, 'ai-interaction-log.md');
const requiredOptions = ['agent', 'step', 'prompt', 'summary', 'files'];

function parseArguments(argumentsList) {
  const options = {};

  for (let index = 0; index < argumentsList.length; index += 1) {
    const argument = argumentsList[index];
    if (!argument.startsWith('--')) {
      throw new Error(`Unexpected argument: ${argument}`);
    }

    const optionName = argument.slice(2);
    if (!requiredOptions.includes(optionName)) {
      throw new Error(`Unknown option: ${argument}`);
    }
    if (Object.hasOwn(options, optionName)) {
      throw new Error(`Option --${optionName} may only be provided once`);
    }

    const optionValue = argumentsList[index + 1];
    if (optionValue === undefined || optionValue.startsWith('--')) {
      throw new Error(`Option --${optionName} requires a value`);
    }

    options[optionName] = optionValue;
    index += 1;
  }

  const missingOptions = requiredOptions.filter((optionName) => !Object.hasOwn(options, optionName));
  if (missingOptions.length > 0) {
    throw new Error(`Missing required options: ${missingOptions.map((name) => `--${name}`).join(', ')}`);
  }

  for (const optionName of requiredOptions.filter((name) => name !== 'files')) {
    if (options[optionName].trim().length === 0) {
      throw new Error(`Option --${optionName} must not be empty`);
    }
  }

  return options;
}

function quoteMarkdown(value) {
  return value
    .replaceAll('\\', '\\\\')
    .replaceAll('`', '\\`')
    .replaceAll('|', '\\|');
}

function asBlockquote(value) {
  return value.split(/\r?\n/).map((line) => `> ${quoteMarkdown(line)}`).join('\n');
}

function formatEntry(options) {
  const timestamp = new Date().toISOString();
  const files = options.files
    .split(/\r?\n/)
    .map((filePath) => filePath.trim())
    .filter(Boolean);
  const fileList = files.length > 0
    ? files.map((filePath) => `- \`${quoteMarkdown(filePath)}\``).join('\n')
    : '- None';

  return [
    `## ${timestamp}`,
    '',
    `- **Agent:** ${quoteMarkdown(options.agent.trim())}`,
    `- **Step:** ${quoteMarkdown(options.step.trim())}`,
    '- **Prompt:**',
    asBlockquote(options.prompt.trim()),
    '- **Summary:**',
    asBlockquote(options.summary.trim()),
    '- **Files:**',
    fileList,
    '',
  ].join('\n');
}

try {
  const options = parseArguments(process.argv.slice(2));
  fs.appendFileSync(logPath, `\n${formatEntry(options)}`, { encoding: 'utf8' });
} catch (error) {
  console.error(`Unable to append AI interaction log: ${error.message}`);
  process.exitCode = 1;
}
