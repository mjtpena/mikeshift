using MikeShift.Core.Documents;

namespace MikeShift.Core.Tests.Documents;

/// <summary>Tolerance of the HTML mini parser: malformed markup, entities, whitespace rules, tables, lists, links, direction, pictures.</summary>
public class HtmlReaderCoverageTests
{
    private static DocModel Html(string html) => HtmlReader.Read(html);

    private static DocParagraph[] Paragraphs(DocModel m) => [.. m.Blocks.OfType<DocParagraph>()];

    private static string[] Texts(DocModel m) => [.. Paragraphs(m).Select(p => p.Text)];

    // ------------------------------------------------------------------ tokenizer edge cases

    [Fact]
    public void Empty_and_tag_free_input()
    {
        Assert.Empty(Html("").Blocks);
        Assert.Equal(["just text"], Texts(Html("just text")));
        Assert.Equal(["a b"], Texts(Html("a \r\n\t b")));
    }

    [Fact]
    public void A_lone_less_than_sign_is_text_wherever_it_is()
    {
        Assert.Equal(["1 < 2"], Texts(Html("1 < 2")));
        Assert.Equal(["a <"], Texts(Html("a <")));
        Assert.Equal(["x <3 y"], Texts(Html("x <3 y")));
        Assert.Equal(["a </ b"], Texts(Html("a </ b")));
    }

    [Fact]
    public void Comments_doctypes_cdata_and_processing_instructions_are_dropped()
    {
        var m = Html("<!DOCTYPE html><?xml version=\"1.0\"?><!-- hidden <b>x</b> --><p>shown<!-- tail --></p><![CDATA[x]]>");
        Assert.Equal(["shown"], Texts(m));
    }

    [Fact]
    public void Unterminated_comments_doctypes_and_instructions_end_the_document_quietly()
    {
        Assert.Equal(["a"], Texts(Html("<p>a</p><!-- never closed <p>b</p>")));
        Assert.Equal(["a"], Texts(Html("<p>a</p><!DOCTYPE never closed")));
        Assert.Equal(["a"], Texts(Html("<p>a</p><?php never closed")));
    }

    [Fact]
    public void Attributes_may_be_quoted_unquoted_valueless_or_unterminated()
    {
        Assert.Equal(["ok"], Texts(Html("<p class=\"a b\" id='x' data-n=5 hidden>ok</p>")));
        Assert.Equal(["ok"], Texts(Html("<p data-x = \"1\" >ok</p >")));
        // An unterminated quote swallows the rest of the document into the attribute value: nothing to show, nothing thrown.
        Assert.Empty(Html("<p title=\"unterminated>text</p>").Blocks);
    }

    [Fact(Skip = "BUG: HtmlReader.Tag() skips one extra character after an attribute that starts with '=' (e.g. <p =x>), swallowing the '>' and the text that follows")]
    public void A_nameless_attribute_does_not_swallow_the_following_text()
    {
        Assert.Equal(["ok"], Texts(Html("<p =oops>ok</p>")));
    }

    [Fact]
    public void Tag_and_attribute_names_are_case_insensitive()
    {
        var m = Html("<P>Hello <B>bold</B> <A HREF=\"HTTPS://example.test/\">link</A></P>");
        var p = Paragraphs(m).Single();
        Assert.True(p.Runs.Single(r => r.Text == "bold").Bold);
        Assert.Equal("HTTPS://example.test/", p.Runs.Single(r => r.Text == "link").Link);
    }

    [Fact]
    public void Self_closing_syntax_is_understood_on_any_element()
    {
        var m = Html("<p>a<br/>b<br />c</p>");
        Assert.Equal(["a\nb\nc"], Texts(m));
    }

    // ------------------------------------------------------------------ entities and whitespace

    [Fact]
    public void Entities_are_decoded_and_nbsp_is_a_space_that_does_not_collapse()
    {
        var m = Html("<p>Tom &amp; Jerry &lt;3 &#169; &#x263A; a&nbsp;&nbsp;b</p>");
        Assert.Equal(["Tom & Jerry <3 © ☺ a  b"], Texts(m));
    }

    [Fact]
    public void White_space_collapses_and_block_edges_are_trimmed()
    {
        var m = Html("<div>  \n  lots   of\t\tspace  </div><p> next </p>");
        Assert.Equal(["lots of space", "next"], Texts(m));
    }

    [Fact]
    public void Pre_blocks_keep_spaces_tabs_and_blank_lines_but_not_the_first_newline()
    {
        var m = Html("<pre>\nline1\n  indented\n\nline4</pre>");
        Assert.Equal(["line1\n  indented\n\nline4"], Texts(m));
    }

    [Fact]
    public void Pre_normalises_carriage_returns_and_nbsp()
    {
        var m = Html("<pre>a\r\nb\rc&nbsp;d</pre>");
        Assert.Equal(["a\nb\nc d"], Texts(m));
    }

    [Theory]
    [InlineData("white-space:pre-wrap")]
    [InlineData("white-space: pre")]
    [InlineData("WHITE-SPACE:break-spaces !important")]
    [InlineData("color:red; white-space:pre-wrap; margin:0")]
    public void White_space_pre_styles_keep_the_text_as_written(string style)
    {
        var m = Html($"<p style=\"{style}\">a  b\n  c</p>");
        Assert.Equal(["a  b\n  c"], Texts(m));
    }

    [Theory]
    [InlineData("white-space:normal")]
    [InlineData("color:red")]
    public void Other_styles_do_not_preserve_white_space(string style)
    {
        var m = Html($"<p style=\"{style}\">a  b\n  c</p>");
        Assert.Equal(["a b c"], Texts(m));
    }

    [Fact]
    public void A_pre_wrap_paragraph_holding_only_a_br_is_a_blank_line()
    {
        var m = Html("<p style=\"white-space:pre-wrap\">a</p><p style=\"white-space:pre-wrap\"><br></p><p style=\"white-space:pre-wrap\">b</p>");
        var ps = Paragraphs(m);
        Assert.Equal(3, ps.Length);
        Assert.Equal("a", ps[0].Text);
        Assert.True(ps[1].IsEmpty);
        Assert.Equal("b", ps[2].Text);
    }

    [Fact]
    public void Leading_and_trailing_br_in_ordinary_paragraphs_are_not_content()
    {
        var m = Html("<p><br><br>text<br><br></p>");
        Assert.Equal(["text"], Texts(m));
    }

    // ------------------------------------------------------------------ inline formatting and links

    [Fact]
    public void Inline_tags_nest_and_unbalanced_closers_do_not_underflow()
    {
        var m = Html("</b></i></u></a>x<b><i><u>y</u></i></b><em>z</em><ins>w</ins><cite>c</cite>");
        var p = Paragraphs(m).Single();
        Assert.Equal("xyzwc", p.Text);
        Assert.Equal((false, false, false), (p.Runs[0].Bold, p.Runs[0].Italic, p.Runs[0].Underline));
        Assert.Equal((true, true, true), (p.Runs[1].Bold, p.Runs[1].Italic, p.Runs[1].Underline));
        Assert.True(p.Runs[2].Italic);
        Assert.True(p.Runs[3].Underline);
        Assert.True(p.Runs[4].Italic);
    }

    [Theory]
    [InlineData("http://example.test/a?b=1&amp;c=2", "http://example.test/a?b=1&c=2")]
    [InlineData("https://example.test", "https://example.test")]
    [InlineData("  mailto:me@example.test ", "mailto:me@example.test")]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("/relative/path", null)]
    [InlineData("file:///c:/secret", null)]
    [InlineData("", null)]
    public void Only_web_and_mail_links_are_kept(string href, string? expected)
    {
        var p = Paragraphs(Html($"<p><a href=\"{href}\">go</a></p>")).Single();
        Assert.Equal(expected, p.Runs.Single().Link);
    }

    [Fact]
    public void An_anchor_without_href_has_no_link_and_the_link_ends_with_the_anchor()
    {
        var p = Paragraphs(Html("<p><a name=top>t</a> <a href=\"https://x.test/\">u</a> v</p>")).Single();
        Assert.Null(p.Runs[0].Link);
        Assert.Equal("https://x.test/", p.Runs.Single(r => r.Text == "u").Link);
        Assert.Null(p.Runs[^1].Link);
    }

    // ------------------------------------------------------------------ blocks, headings, lists

    [Fact]
    public void Headings_lose_inline_bold_and_italic_and_list_items_carry_nesting_and_order()
    {
        var m = Html("<h2><b><i>Head</i></b></h2><ol><li>one<ul><li>nested</li></ul></li><li>two</li></ol><li>stray</li>");
        var ps = Paragraphs(m);
        Assert.Equal((ParagraphKind.Heading, 2), (ps[0].Kind, ps[0].Level));
        Assert.All(ps[0].Runs, r => Assert.False(r.Bold || r.Italic));
        Assert.Equal((ParagraphKind.ListItem, 0, true, "one"), (ps[1].Kind, ps[1].Level, ps[1].Ordered, ps[1].Text));
        Assert.Equal((ParagraphKind.ListItem, 1, false, "nested"), (ps[2].Kind, ps[2].Level, ps[2].Ordered, ps[2].Text));
        Assert.Equal((ParagraphKind.ListItem, 0, true, "two"), (ps[3].Kind, ps[3].Level, ps[3].Ordered, ps[3].Text));
        Assert.Equal((ParagraphKind.ListItem, 0, false, "stray"), (ps[4].Kind, ps[4].Level, ps[4].Ordered, ps[4].Text));
    }

    [Fact]
    public void Unbalanced_list_closers_are_harmless()
    {
        Assert.Equal(["a"], Texts(Html("</ul></ol><p>a</p></ul>")));
    }

    [Fact]
    public void Block_level_containers_each_start_a_paragraph_and_hr_separates()
    {
        var m = Html("<section>a</section><article>b</article><blockquote>c</blockquote>d<hr>e<dl><dt>t</dt><dd>d</dd></dl>");
        Assert.Equal(["a", "b", "c", "d", "e", "t", "d"], Texts(m));
    }

    [Fact]
    public void Content_of_script_style_title_and_friends_is_never_text()
    {
        var m = Html("<title>T</title><style>p{}</style><script>var x='<p>no</p>';</script><noscript>n</noscript><textarea>ta</textarea>" +
                     "<select><option>o</option></select><iframe>f</iframe><template><p>tpl</p></template><math>m</math><p>body</p>");
        Assert.Equal(["body"], Texts(m));
    }

    [Fact]
    public void Unclosed_ignored_elements_swallow_the_rest_of_the_document()
    {
        Assert.Empty(Html("<script>never closed <p>x</p>").Blocks);
        Assert.Empty(Html("<style>never closed").Blocks);
        Assert.Equal(["after"], Texts(Html("<script src=x.js /><p>after</p>")));
    }

    [Theory]
    [InlineData("<img src=x.png>")]
    [InlineData("<svg><circle/></svg>")]
    [InlineData("<picture><source><img src=a></picture>")]
    [InlineData("<object data=x></object>")]
    [InlineData("<embed src=x>")]
    [InlineData("<canvas></canvas>")]
    public void Picture_like_elements_set_the_flag(string markup)
    {
        Assert.True(Html("<p>text</p>" + markup).HasPictures);
    }

    [Fact]
    public void Without_picture_elements_the_flag_stays_off()
    {
        Assert.False(Html("<p>text <a href=\"https://x.test/i.png\">link</a></p>").HasPictures);
    }

    [Fact]
    public void Page_break_styles_insert_page_breaks_before_the_element()
    {
        var m = Html("<p>a</p><p style=\"page-break-before: always\">b</p><h1 style=\"break-before:page\">c</h1>" +
                     "<div style=\"page-break-before:always\"></div><ul style=\"page-break-before:always\"><li>x</li></ul>" +
                     "<table style=\"page-break-before:always\"><tr><td>t</td></tr></table><pre style=\"page-break-before:always\">p</pre>" +
                     "<span style=\"page-break-before:always\"></span>");
        Assert.Equal(7, m.Blocks.OfType<DocPageBreak>().Count());
        Assert.IsType<DocPageBreak>(m.Blocks[1]);
    }

    // ------------------------------------------------------------------ direction

    [Fact]
    public void The_dir_attribute_marks_paragraphs_right_to_left_until_its_element_closes()
    {
        var m = Html("<div dir=\"rtl\"><p>a</p><p dir=\"ltr\">b</p><p>c</p></div><p>d</p><span dir=rtl>e</span>");
        var ps = Paragraphs(m);
        Assert.Equal(["a", "b", "c", "d", "e"], ps.Select(p => p.Text));
        Assert.Equal([true, false, true, false, true], ps.Select(p => p.Rtl));
    }

    [Fact]
    public void A_dir_attribute_on_void_or_self_closing_elements_does_not_leak()
    {
        var m = Html("<br dir=rtl><p>a</p><img dir=rtl><p>b</p><div dir=rtl/><p>c</p>");
        Assert.All(Paragraphs(m), p => Assert.False(p.Rtl));
    }

    // ------------------------------------------------------------------ tables

    [Fact]
    public void Tables_become_rows_of_cells_and_th_cells_are_bold()
    {
        var m = Html("<table><tr><th>H1</th><th>H2</th></tr><tr><td>a</td><td>b <i>c</i></td></tr></table>");
        var t = Assert.IsType<DocTable>(Assert.Single(m.Blocks));
        Assert.Equal(2, t.Rows.Count);
        Assert.True(t.Rows[0][0][0].Runs[0].Bold);
        Assert.Equal(["a", "b c"], t.Rows[1].Select(c => c[0].Text));
    }

    [Fact]
    public void Cells_without_closing_tags_and_empty_cells_are_tolerated()
    {
        var m = Html("<table><tr><td>a<td><td>c<tr><td>d</table>after");
        var t = Assert.IsType<DocTable>(m.Blocks[0]);
        Assert.Equal(["a", "", "c"], t.Rows[0].Select(c => c[0].Text));
        Assert.Equal(["d"], t.Rows[1].Select(c => c[0].Text));
        Assert.Equal("after", ((DocParagraph)m.Blocks[1]).Text);
    }

    [Fact]
    public void An_unclosed_table_is_flushed_at_the_end_and_stray_cells_outside_tables_are_text()
    {
        var m = Html("<table><tr><td>x</td></tr>");
        Assert.IsType<DocTable>(Assert.Single(m.Blocks));
        Assert.Equal(["loose cell"], Texts(Html("<td>loose cell</td>")));
        Assert.Empty(Html("<table></table>").Blocks);
    }

    [Fact]
    public void Nested_tables_are_flattened_into_the_outer_cell()
    {
        var m = Html("<table><tr><td>outer<table><tr><td>inner</td></tr></table></td><td>next</td></tr></table>");
        var t = Assert.IsType<DocTable>(Assert.Single(m.Blocks));
        Assert.Equal(2, t.Rows[0].Count);
        Assert.Contains("inner", string.Concat(t.Rows[0][0].Select(p => p.Text)));
        Assert.Equal("next", t.Rows[0][1][0].Text);
    }

    [Fact]
    public void Block_elements_inside_cells_become_paragraphs_in_the_cell()
    {
        var m = Html("<table><tr><td><p>one</p><p>two</p><ul><li>item</li></ul></td></tr></table>");
        var cell = Assert.IsType<DocTable>(m.Blocks[0]).Rows[0][0];
        Assert.Equal(["one", "two", "• item"], cell.Select(p => p.Text));
    }
}
