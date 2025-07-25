/**
 * Lightweight Markdown Parser
 * Handles basic markdown formatting for job output display
 */
export class MarkdownParser {
  static parse(markdown) {
    if (!markdown || typeof markdown !== 'string') {
      return '';
    }

    let html = markdown;

    // Escape HTML characters first to prevent XSS
    html = html
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;');

    // Code blocks (```code```)
    html = html.replace(/```(\w*)\n?([\s\S]*?)```/g, (match, lang, code) => {
      const language = lang ? ` class="language-${lang}"` : '';
      return `<pre class="code-block"><code${language}>${code.trim()}</code></pre>`;
    });

    // Inline code (`code`)
    html = html.replace(/`([^`]+)`/g, '<code class="inline-code">$1</code>');

    // Headers
    html = html.replace(/^### (.*$)/gim, '<h3 class="md-h3">$1</h3>');
    html = html.replace(/^## (.*$)/gim, '<h2 class="md-h2">$1</h2>');
    html = html.replace(/^# (.*$)/gim, '<h1 class="md-h1">$1</h1>');

    // Bold text (**text**)
    html = html.replace(/\*\*([^*]+)\*\*/g, '<strong class="md-bold">$1</strong>');

    // Italic text (*text*)
    html = html.replace(/\*([^*]+)\*/g, '<em class="md-italic">$1</em>');

    // Links [text](url)
    html = html.replace(/\[([^\]]+)\]\(([^)]+)\)/g, '<a href="$2" class="md-link" target="_blank" rel="noopener noreferrer">$1</a>');

    // Unordered lists
    html = html.replace(/^\* (.+)$/gim, '<li class="md-list-item">$1</li>');
    html = html.replace(/(<li class="md-list-item">.*<\/li>)/s, '<ul class="md-list">$1</ul>');

    // Ordered lists
    html = html.replace(/^\d+\. (.+)$/gim, '<li class="md-list-item">$1</li>');
    
    // Handle line breaks - convert double newlines to paragraphs
    html = html.split('\n\n').map(paragraph => {
      paragraph = paragraph.trim();
      if (!paragraph) return '';
      
      // Don't wrap if it's already a block element
      if (paragraph.startsWith('<h') || paragraph.startsWith('<pre') || 
          paragraph.startsWith('<ul') || paragraph.startsWith('<ol') ||
          paragraph.startsWith('<li')) {
        return paragraph;
      }
      
      return `<p class="md-paragraph">${paragraph}</p>`;
    }).join('\n');

    // Convert single newlines to <br>
    html = html.replace(/\n/g, '<br>');

    // Clean up any empty paragraphs
    html = html.replace(/<p class="md-paragraph"><\/p>/g, '');

    return html;
  }

  static renderToElement(markdown, container) {
    const html = this.parse(markdown);
    container.innerHTML = html;
    
    // Add syntax highlighting to code blocks if available
    this.highlightCode(container);
    
    return container;
  }

  static highlightCode(container) {
    const codeBlocks = container.querySelectorAll('code[class*="language-"]');
    codeBlocks.forEach(block => {
      // Basic syntax highlighting for common languages
      const lang = block.className.match(/language-(\w+)/)?.[1];
      if (lang) {
        this.applySyntaxHighlighting(block, lang);
      }
    });
  }

  static applySyntaxHighlighting(element, language) {
    let content = element.textContent;
    
    // Basic JavaScript/TypeScript highlighting
    if (['javascript', 'js', 'typescript', 'ts'].includes(language)) {
      content = content
        .replace(/\b(const|let|var|function|class|if|else|for|while|return|import|export|from|default)\b/g, 
                '<span class="syntax-keyword">$1</span>')
        .replace(/"([^"]*)"/g, '<span class="syntax-string">"$1"</span>')
        .replace(/'([^']*)'/g, '<span class="syntax-string">\'$1\'</span>')
        .replace(/\/\/.*$/gm, '<span class="syntax-comment">$&</span>')
        .replace(/\/\*[\s\S]*?\*\//g, '<span class="syntax-comment">$&</span>')
        .replace(/\b(\d+)\b/g, '<span class="syntax-number">$1</span>');
    }
    
    // Basic Python highlighting
    else if (['python', 'py'].includes(language)) {
      content = content
        .replace(/\b(def|class|if|elif|else|for|while|return|import|from|try|except|finally|with|as)\b/g, 
                '<span class="syntax-keyword">$1</span>')
        .replace(/"([^"]*)"/g, '<span class="syntax-string">"$1"</span>')
        .replace(/'([^']*)'/g, '<span class="syntax-string">\'$1\'</span>')
        .replace(/#.*$/gm, '<span class="syntax-comment">$&</span>')
        .replace(/\b(\d+)\b/g, '<span class="syntax-number">$1</span>');
    }
    
    // Basic JSON highlighting
    else if (['json'].includes(language)) {
      content = content
        .replace(/"([^"]*)":/g, '<span class="syntax-property">"$1"</span>:')
        .replace(/:\s*"([^"]*)"/g, ': <span class="syntax-string">"$1"</span>')
        .replace(/:\s*(\d+)/g, ': <span class="syntax-number">$1</span>')
        .replace(/:\s*(true|false|null)/g, ': <span class="syntax-keyword">$1</span>');
    }
    
    element.innerHTML = content;
  }
}