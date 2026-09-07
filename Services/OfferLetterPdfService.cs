using ExamPortal.Models;
using Microsoft.AspNetCore.Hosting;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ExamPortal.Services
{
    /// <summary>
    /// Generates the Vistawaystech LLP offer-of-employment PDF using QuestPDF —
    /// letterhead, the 14 standard clauses, Candidate Undertaking, and
    /// Annexures I (IP), II (Confidentiality), and III (Compensation Structure).
    /// Call GeneratePdf(offer, candidate) to get raw PDF bytes.
    /// </summary>
    public class OfferLetterPdfService
    {
        private readonly IWebHostEnvironment _env;

        public OfferLetterPdfService(IWebHostEnvironment env)
        {
            _env = env;
        }

        // ── Static company details (same for every letter) ─────────────────
        private const string CompanyName = "Vistawaystech LLP";
        private const string CompanyAddressLine1 = "#801 & 802, Manjeera Majestic";
        private const string CompanyAddressLine2 = "Commercials, Hitech city Road,";
        private const string CompanyAddressLine3 = "OPP JNTU, Hyderabad";
        private const string CompanyAddressLine4 = "MEDCHAL MALKAJGIRI";
        private const string CompanyAddressLine5 = "TELANGANA";
        private const string CompanyPincode = "500085";
        private const string HrContactEmail = "vijayakiron.abbineni@vistawaystech.com";
        private const string SignatoryName = "Vijayakiron Abbineni";
        private const string SignatoryTitle = "CEO & Partner";

        public byte[] GeneratePdf(OfferLetter offer, User candidate)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            byte[]? logoBytes = null;
            try
            {
                var logoPath = Path.Combine(_env.WebRootPath ?? "wwwroot", "logo.png");
                if (File.Exists(logoPath)) logoBytes = File.ReadAllBytes(logoPath);
            }
            catch { /* logo is decorative; letter still generates without it */ }

            var refNumber = $"VIST01/{offer.IssuedAt:yyyy}/{offer.Id:D5}";
            var validUntil = offer.OfferValidUntil ?? offer.IssuedAt.AddDays(7);

            var doc = Document.Create(container =>
            {
                // ══════════════════════════════════════════════════════════
                // PAGE 1 — Letterhead, clauses 1–5
                // ══════════════════════════════════════════════════════════
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(45);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial").LineHeight(1.25f));

                    page.Header().Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Row(inner =>
                            {
                                if (logoBytes != null)
                                {
                                    inner.ConstantItem(42).Height(42).Image(logoBytes).FitArea();
                                    inner.ConstantItem(6);
                                }
                                inner.RelativeItem().AlignMiddle().Column(c =>
                                {
                                    c.Item().Text("VISTAWAYS TECH").Bold().FontSize(15);
                                    c.Item().Text("INNOVATION. INTEGRATION. IMPACT.").FontSize(6).FontColor("#6b7280");
                                });
                            });
                            row.ConstantItem(190).AlignRight().Column(c =>
                            {
                                c.Item().Text(CompanyName).Bold().FontSize(9);
                                c.Item().Text(CompanyAddressLine1).FontSize(8);
                                c.Item().Text(CompanyAddressLine2).FontSize(8);
                                c.Item().Text(CompanyAddressLine3).FontSize(8);
                                c.Item().Text(CompanyAddressLine4).FontSize(8);
                                c.Item().Text(CompanyAddressLine5).FontSize(8);
                                c.Item().Text(CompanyPincode).FontSize(8);
                            });
                        });
                        col.Item().PaddingTop(8).Height(4);
                    });

                    page.Footer().Row(row =>
                    {
                        row.RelativeItem().Text("");
                        row.ConstantItem(100).AlignRight().Text(x =>
                        {
                            x.Span("Page ").FontSize(8).FontColor("#9ca3af");
                            x.CurrentPageNumber().FontSize(8).FontColor("#9ca3af");
                            x.Span(" of ").FontSize(8).FontColor("#9ca3af");
                            x.TotalPages().FontSize(8).FontColor("#9ca3af");
                        });
                    });

                    page.Content().Column(col =>
                    {
                        col.Item().Text($"Ref: {refNumber}").FontSize(9);
                        col.Item().Text($"Date: {offer.IssuedAt:dd MMM yyyy}").FontSize(9);
                        col.Item().Height(10);

                        col.Item().Text(candidate.FullName).Bold();
                        WriteCandidateAddress(col, candidate);
                        col.Item().Height(12);

                        col.Item().AlignCenter().Text("Offer of Employment").Bold().FontSize(12);
                        col.Item().Height(10);

                        col.Item().Text(txt =>
                        {
                            txt.Span("Dear Mr. ");
                            txt.Span(candidate.FullName).Bold();
                        });
                        col.Item().Height(8);

                        col.Item().Text(txt =>
                        {
                            txt.Span("Further to our discussions, we are pleased to appoint you as ");
                            txt.Span(offer.Designation).Bold();
                            txt.Span($" with {CompanyName}, as per the terms and conditions stated below:");
                        });
                        col.Item().Height(10);

                        // ── Clause 1 ──
                        Para(col, txt =>
                        {
                            txt.Span("1. ").Bold();
                            txt.Span("Your offer letter is valid till ").Bold();
                            txt.Span($"{validUntil:dd-MM-yyyy}").Bold();
                            txt.Span(" and you are required to confirm acceptance of the offer and join on ").Bold();
                            txt.Span($"{offer.JoiningDate:dd-MM-yyyy}").Bold();
                            txt.Span(". If you do not confirm your acceptance, this offer is treated as withdrawn. " +
                                     "To confirm your acceptance of this offer, you are required to respond via email to ").Bold();
                            txt.Span(HrContactEmail).Bold();
                            txt.Span(".").Bold();
                        });

                        // ── Clause 2 ──
                        Heading(col, "2. Reporting and Responsibilities:");
                        Para(col, txt =>
                        {
                            txt.Span("You will report to the undersigned and complete the joining formalities. Your working location will be at ");
                            txt.Span(offer.Location).Bold();
                            txt.Span(". We will communicate you about your reporting manager on joining the organization and completing the joining formalities.");
                        });
                        Para(col, txt =>
                        {
                            txt.Span("Note: ").Bold();
                            txt.Span("On your joining date, please bring Soft copies of (i) the original offer letter duly signed and " +
                                     "dated by you; (ii) Passport size photograph (Soft copy), (iii) the originals and 1 set of soft copies of the following documents:");
                        });
                        Bullets(col, new[]
                        {
                            "Education degree certificate",
                            "Relieving letter or resignation acceptance letter from your most recent employer",
                            "Proof of identity. Bring the following documents: AADHAR card/Passport and PAN card (mandatory)"
                        });
                        Para(col, "Please note that all the above documents are mandatory, and you will not be allowed to join without them.", bold: true);

                        // ── Clause 3 ──
                        Heading(col, "3. Compensation");
                        Para(col, txt =>
                        {
                            var ctcRupees = offer.CtcLpa * 100000m;
                            txt.Span("During your probation period, you will be eligible for a compensation of INR ");
                            txt.Span($"{ctcRupees:N0} ({AmountToIndianWords(ctcRupees)})").Bold();
                            txt.Span(" per annum as CTC (Cost to Company) as mentioned in the annexure III. Your compensation and " +
                                     "other benefits, if any, shall be subject to the deductions of all Governmental and local taxes, " +
                                     "statutory contribution, etc. as required to be made under the laws of India and shall be paid in accordance with the practices of the Company.");
                        });

                        // ── Clause 4 ──
                        Heading(col, "4. Working hours");
                        LetteredList(col, new[]
                        {
                            "Your working hours are from 9:30 am to 6:30 pm, Monday to Friday",
                            "The Company reserves the right to require you to work outside your normal working hours if necessary, in furtherance of your duties.",
                            "You will be eligible for leave and other benefits as per the rules of the company."
                        });

                        // ── Clause 5 ──
                        Heading(col, "5. Responsibilities");
                        Para(col, "You must effectively, diligently and to the best of your ability perform all responsibilities and " +
                                  "duties and ensure successful completion of the assignments given to you time to time by your reporting manager.");
                    });
                });

                // ══════════════════════════════════════════════════════════
                // PAGE 2 — Clauses 6–14, signature, Candidate Undertaking
                // ══════════════════════════════════════════════════════════
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(45);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial").LineHeight(1.25f));
                    page.Footer().AlignRight().Text(x =>
                    {
                        x.Span("Page ").FontSize(8).FontColor("#9ca3af");
                        x.CurrentPageNumber().FontSize(8).FontColor("#9ca3af");
                        x.Span(" of ").FontSize(8).FontColor("#9ca3af");
                        x.TotalPages().FontSize(8).FontColor("#9ca3af");
                    });

                    page.Content().Column(col =>
                    {
                        Heading(col, "6. Non-disclosure obligations and intellectual property");
                        Para(col, ClauseText.NonDisclosureIp);

                        Heading(col, "7. Confidentiality");
                        Para(col, ClauseText.Confidentiality);

                        Heading(col, "8. Company property");
                        Para(col, ClauseText.CompanyPropertyPart1);
                        Para(col, ClauseText.CompanyPropertyPart2);

                        Heading(col, "9. Notice of Change");
                        Para(col, ClauseText.NoticeOfChange);

                        Heading(col, "10. Termination");
                        Para(col, "You are entitled to receive from the Company and you are required to give the Company the " +
                                  "following period of notice, in writing, to terminate your employment without cause, such notice, in each case, to expire as stated below:");
                        LetteredList(col, new[]
                        {
                            "During the probation period, either party may terminate this contract by giving 30 days notice in writing or payment in lieu of salary (net of provident fund contribution). However, the Company reserves the right not to accept payment in lieu of notice and at its sole discretion and enforce the notice period.",
                            "Your employment may be terminated forthwith by the Company without prior notice if any declaration/statement or information forthwith given by you in your application or in connection with your contract at any time found to be false or untrue or any material particulars are suppressed.",
                            "Further the Company shall have the right to terminate your services without any notice or salary in lieu thereof for misconduct, negligence of duty, disloyalty, dishonesty, indiscipline, disobedience, irregular attendance, or long period of absence from duty due to ill-health, infirmity or accident or inefficiency.",
                            "Without limiting the general effect of clause 10 (c), if you absent yourself without leave or remain absent beyond the period of leave originally granted or subsequently extended, you shall be considered as having voluntarily terminated your employment without giving any notice.",
                            "After your confirmation, the notice period for both sides shall be 60 days or pay in lieu of the same. And on any such failure from employee's side, company will be forced to take necessary legal action against you in a court of law and you alone will be held responsible for all the costs, compensation, loss or legal consequences if any. Further, in such circumstances, issuance of relieving letter, conduct certificate, experience certificate etc will be solely at the discretion of the company.",
                            "Upon separation from the company, you shall hand over all company's property under your custody to your reporting manager."
                        });

                        Heading(col, "11. Exclusivity / Prior Commitment");
                        Para(col, ClauseText.Exclusivity);

                        Heading(col, "12. Non-Compete / Non-Solicit");
                        Para(col, ClauseText.NonCompeteIntro, bold: true);
                        Para(col, ClauseText.NonCompete);
                        Para(col, ClauseText.NonSolicitCustomers, bold: true);
                        Para(col, ClauseText.NonSolicitCustomersBody);
                        Para(col, ClauseText.NonSolicitEmployees, bold: true);
                        Para(col, ClauseText.NonSolicitEmployeesBody);

                        Heading(col, "13. Notices");
                        Para(col, txt =>
                        {
                            txt.Span("All notices required to be given hereunder shall be given in writing, by personal delivery or by mail " +
                                     "and confirmed by fax at the respective addresses of the parties hereto set forth above, or at such " +
                                     "address as may be designated in writing by either party, and in the case of the Company, to: " +
                                     $"{CompanyName}, {CompanyAddressLine1} {CompanyAddressLine2} {CompanyAddressLine3} or Email to : ");
                            txt.Span(HrContactEmail).Bold();
                        });

                        Heading(col, "14. Jurisdiction");
                        Para(col, ClauseText.Jurisdiction);

                        col.Item().Height(20);
                        col.Item().Text("With best wishes,");
                        col.Item().Text($"For {CompanyName}");
                        col.Item().Height(28);
                        col.Item().Text(SignatoryName).Bold();
                        col.Item().Text(SignatoryTitle).FontSize(9);

                        col.Item().Height(20);
                        col.Item().Text("Candidate Undertaking:").Bold();
                        Para(col, "I have carefully read and understood the terms and conditions mentioned above and in the Annexure I and II attached.");
                        Para(col, "I acknowledge that while I am working for " + CompanyName + ", I will take proper care of all company " +
                                  "equipment that I am entrusted with. I further understand that upon termination, I will return all company " +
                                  "property and that the property will be returned in proper working order. I understand I may be held financially " +
                                  "responsible for lost or damaged property. This agreement includes, but is not limited to, laptops, cell phones and other equipment. " +
                                  "I understand that failure to return equipment will be considered theft and may lead to criminal prosecution by Company.");
                        Para(col, "I accept all the terms and conditions mentioned therein. I shall commence my probation with effect from");
                        col.Item().Height(20);
                        SignatureLine(col);
                    });
                });

                // ══════════════════════════════════════════════════════════
                // PAGE 3 — Annexure I & II
                // ══════════════════════════════════════════════════════════
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(45);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial").LineHeight(1.25f));
                    page.Footer().AlignRight().Text(x =>
                    {
                        x.Span("Page ").FontSize(8).FontColor("#9ca3af");
                        x.CurrentPageNumber().FontSize(8).FontColor("#9ca3af");
                        x.Span(" of ").FontSize(8).FontColor("#9ca3af");
                        x.TotalPages().FontSize(8).FontColor("#9ca3af");
                    });

                    page.Content().Column(col =>
                    {
                        col.Item().Text("Annexure I:").Bold();
                        col.Item().Text("Intellectual property").Bold();
                        col.Item().Height(8);
                        Para(col, ClauseText.AnnexureIIntro);
                        NumberedList(col, new[]
                        {
                            ClauseText.AnnexureIItem1,
                            ClauseText.AnnexureIItem2,
                            ClauseText.AnnexureIItem3
                        });
                        col.Item().Height(16);
                        SignatureLine(col);

                        col.Item().Height(24);
                        col.Item().Text("Annexure II:").Bold();
                        col.Item().Text("Confidential Information").Bold();
                        col.Item().Height(8);
                        Para(col, ClauseText.AnnexureIIIntro);
                        col.Item().Text("i. Information of value or significance to the Company, its subsidiaries, divisions, affiliates, customers or its competitors (present or potential) such as:");
                        Bullets(col, new[]
                        {
                            "Customer data, in particular, key contact names, addresses, sales figures and sales conditions of the Company and its past, present or prospective clients.",
                            "Business data, particularly data relating to new products, promotion campaigns, distribution strategies, license agreements and joint ventures in which the Company is involved.",
                            "Software data, particularly information relating to the software and the modules thereof as well as any devices designed by the Company to prevent unauthorized copying.",
                            "Research and development data, particularly information relating to the software and hardware developments of the Company.",
                            "Financial data, in particular, concerning budgets, the fees and revenue calculations, sales figures, financial statements, profit expectations and inventories of the Company.",
                            "Procedures for computer access and passwords to the Company's Confidential Information, due diligence reports and all other documentation normally related to the business of the Company.",
                            "Lists of or information about personnel seeking employment with or who are already employed by the Company.",
                            "Any and all other information or materials or documents of a commercially sensitive nature relating to the Company's operations, research, plans, strategies, objectives, development, purchasing, marketing, and selling activities."
                        });
                        RomanList(col, new[]
                        {
                            "Original information supplied by the Company;",
                            "Information not known to competitors of the Company nor intended by the Company for general dissemination, including but not limited to, policies, strategies, the identity of various product-suppliers or service-providers, billing schedules, needs of its clients, information as to the profitability of specific accounts, and information about the Company itself and its executives, officers, directors and employees;",
                            "Any business or technical information relating to the Company, including but not limited to financial information, equipment, documentation, strategies, marketing plans, prospective leads or target accounts, pricing information, information relating to existing, previous and potential customers and contracts disclosed by the Company to you;",
                            "Any copies of the above-mentioned information; but does not include:"
                        }, startAt: 2);
                        LetteredList(col, new[]
                        {
                            "That which is in the public domain, other than by your breach of this contract or of any other confidentiality agreement.",
                            "That which was previously known as established by your written records prior to receipt from the Company and in your possession prior to the date of this contract;",
                            "That which was lawfully obtained by you from a third party; and",
                            "That which was developed independently by you without reference to the Confidential Information provided by the Company."
                        });
                        col.Item().Height(16);
                        SignatureLine(col);
                    });
                });

                // ══════════════════════════════════════════════════════════
                // PAGE 4 — Annexure III (Compensation Structure)
                // ══════════════════════════════════════════════════════════
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(45);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial").LineHeight(1.25f));
                    page.Footer().AlignRight().Text(x =>
                    {
                        x.Span("Page ").FontSize(8).FontColor("#9ca3af");
                        x.CurrentPageNumber().FontSize(8).FontColor("#9ca3af");
                        x.Span(" of ").FontSize(8).FontColor("#9ca3af");
                        x.TotalPages().FontSize(8).FontColor("#9ca3af");
                    });

                    page.Content().Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().AlignMiddle().Text("Annexure III").Bold();
                            row.ConstantItem(190).AlignRight().Column(c =>
                            {
                                c.Item().Text(CompanyName).Bold().FontSize(9);
                                c.Item().Text(CompanyAddressLine1).FontSize(8);
                                c.Item().Text(CompanyAddressLine2).FontSize(8);
                                c.Item().Text(CompanyAddressLine3).FontSize(8);
                                c.Item().Text(CompanyAddressLine4).FontSize(8);
                                c.Item().Text(CompanyAddressLine5).FontSize(8);
                                c.Item().Text(CompanyPincode).FontSize(8);
                            });
                        });
                        col.Item().Height(14);

                        bool hasBreakup = offer.BasicMonthly > 0 || offer.HraMonthly > 0 || offer.SpecialAllowanceMonthly > 0
                            || offer.EmployeeEpfMonthly > 0 || offer.EmployerEpfMonthly > 0 || offer.PerformanceIncentiveAnnual > 0;

                        if (hasBreakup)
                        {
                            var gross = offer.BasicMonthly + offer.HraMonthly + offer.SpecialAllowanceMonthly;
                            var totalDeductions = offer.EmployeeEpfMonthly + offer.EmployeeEsiMonthly + offer.TrainingChargesMonthly + offer.ProfessionalTaxMonthly;
                            var netMonthly = gross - totalDeductions;
                            var employerTotal = offer.EmployerEpfMonthly + offer.EmployerEsiMonthly;
                            var fixedCtcMonthly = gross + employerTotal;
                            var fixedCtcAnnual = fixedCtcMonthly * 12;
                            var totalCtcAnnual = fixedCtcAnnual + offer.PerformanceIncentiveAnnual;

                            col.Item().Text(candidate.FullName).Bold();
                            col.Item().Text($"Designation: {offer.Designation}    |    Location: {offer.Location}").FontSize(9);
                            col.Item().Height(8);

                            col.Item().Table(table =>
                            {
                                table.ColumnsDefinition(c =>
                                {
                                    c.RelativeColumn(3);
                                    c.RelativeColumn(2);
                                    c.RelativeColumn(1.4f);
                                });

                                void SectionRow(string label)
                                {
                                    table.Cell().ColumnSpan(3).Background("#f1f5f9").Padding(5)
                                        .Text(label).Bold().FontSize(9.5f);
                                }
                                void DataRow(string label, decimal amount, string payable = "", bool bold = false)
                                {
                                    table.Cell().Padding(5).BorderBottom(0.5f).BorderColor("#e2e8f0")
                                        .Text(t => { var s = t.Span(label); if (bold) s.Bold(); s.FontSize(9.5f); });
                                    table.Cell().Padding(5).BorderBottom(0.5f).BorderColor("#e2e8f0").AlignRight()
                                        .Text(t => { var s = t.Span($"{amount:N0}"); if (bold) s.Bold(); s.FontSize(9.5f); });
                                    table.Cell().Padding(5).BorderBottom(0.5f).BorderColor("#e2e8f0")
                                        .Text(t => { var s = t.Span(payable); if (bold) s.Bold(); s.FontSize(9.5f); });
                                }

                                table.Cell().ColumnSpan(2).Background("#f8fafc").Padding(5).Text("Salary component").Bold().FontSize(9.5f);
                                table.Cell().Background("#f8fafc").Padding(5).AlignRight().Text("Amount (INR)").Bold().FontSize(9.5f);

                                DataRow("Basic", offer.BasicMonthly);
                                DataRow("HRA", offer.HraMonthly);
                                DataRow("Special allowance", offer.SpecialAllowanceMonthly);
                                DataRow("Gross Monthly Salary [a]", gross, bold: true);

                                SectionRow("Deductions");
                                DataRow("Employee contribution to EPF", offer.EmployeeEpfMonthly);
                                DataRow("Employee contribution to ESI", offer.EmployeeEsiMonthly);
                                DataRow("Training and Development Charges", offer.TrainingChargesMonthly);
                                DataRow("Professional Tax", offer.ProfessionalTaxMonthly);
                                DataRow("Total Deductions (before TDS) [b]", totalDeductions, bold: true);
                                DataRow("Net Monthly Salary (before TDS) [c]=[a]-[b]", netMonthly, "Per month", bold: true);

                                SectionRow("Employer Contribution:");
                                DataRow("EPF", offer.EmployerEpfMonthly);
                                DataRow("ESI", offer.EmployerEsiMonthly);
                                DataRow("Total Employer Contribution (Monthly) [d]", employerTotal, bold: true);
                                DataRow("Total Fixed CTC (Per Month) [e]=[a]+[d]", fixedCtcMonthly, bold: true);
                                DataRow("Total Fixed CTC (Per Annum) [f]=[e]*12", fixedCtcAnnual, bold: true);
                                DataRow("Performance based incentive (Per Annum)", offer.PerformanceIncentiveAnnual);
                                DataRow("Total CTC (Per Annum) [rounded off]", Math.Round(totalCtcAnnual, 0), bold: true);
                            });

                            col.Item().Height(10);
                            col.Item().Text("NOTE:").Bold().FontSize(9);
                            NumberedList(col, new[]
                            {
                                $"For any queries regarding your salary structure please send an email to: {HrContactEmail}",
                                "Gratuity is a retirement benefit credited to the employee and eligible as per gratuity policy",
                                "Performance based Incentive is payable monthly after completion of probation. Performance evaluation will be carried out once in 6 months."
                            });
                        }
                        else
                        {
                            Para(col, txt =>
                            {
                                txt.Span("Total CTC (Per Annum): INR ");
                                txt.Span($"{offer.CtcLpa * 100000m:N0}").Bold();
                                txt.Span(" for ");
                                txt.Span(candidate.FullName).Bold();
                                txt.Span($", {offer.Designation}. A detailed monthly breakup was not provided with this offer.");
                            });
                        }

                        col.Item().Height(24);
                        col.Item().Text($"For: {CompanyName}").Bold();
                        col.Item().Height(28);
                        col.Item().Text(SignatoryName).Bold();
                        col.Item().Text(SignatoryTitle).FontSize(9);
                    });
                });
            });

            return doc.GeneratePdf();
        }

        // ── Layout helpers ───────────────────────────────────────────────

        private static void WriteCandidateAddress(ColumnDescriptor col, User candidate)
        {
            var addr = candidate.Address;
            if (addr != null && !string.IsNullOrWhiteSpace(addr.AddressLine))
            {
                col.Item().Text(addr.AddressLine);
                var cityState = string.Join(", ", new[] { addr.City, addr.State, addr.Country }.Where(s => !string.IsNullOrWhiteSpace(s)));
                if (!string.IsNullOrWhiteSpace(cityState)) col.Item().Text(cityState);
                if (!string.IsNullOrWhiteSpace(addr.Pincode)) col.Item().Text($"PIN: {addr.Pincode}");
            }
            else if (!string.IsNullOrWhiteSpace(candidate.PermanentAddress))
            {
                col.Item().Text(candidate.PermanentAddress);
            }
        }

        private static void Heading(ColumnDescriptor col, string text)
        {
            col.Item().PaddingTop(8).Text(text).Bold();
            col.Item().Height(3);
        }

        private static void Para(ColumnDescriptor col, string text, bool bold = false)
        {
            var t = col.Item().PaddingTop(4).Text(text);
            if (bold) t.Bold();
        }

        private static void Para(ColumnDescriptor col, Action<TextDescriptor> content)
        {
            col.Item().PaddingTop(4).Text(content);
        }

        private static void Bullets(ColumnDescriptor col, IEnumerable<string> items)
        {
            foreach (var item in items)
            {
                col.Item().PaddingTop(2).PaddingLeft(14).Row(row =>
                {
                    row.ConstantItem(12).Text("•");
                    row.RelativeItem().Text(item);
                });
            }
        }

        private static void LetteredList(ColumnDescriptor col, IEnumerable<string> items)
        {
            char letter = 'a';
            foreach (var item in items)
            {
                col.Item().PaddingTop(3).PaddingLeft(14).Row(row =>
                {
                    row.ConstantItem(16).Text($"{letter}.");
                    row.RelativeItem().Text(item);
                });
                letter++;
            }
        }

        private static void NumberedList(ColumnDescriptor col, IEnumerable<string> items, int startAt = 1)
        {
            int n = startAt;
            foreach (var item in items)
            {
                col.Item().PaddingTop(3).PaddingLeft(14).Row(row =>
                {
                    row.ConstantItem(18).Text($"{n}.");
                    row.RelativeItem().Text(item);
                });
                n++;
            }
        }

        private static readonly string[] RomanNumerals = { "i", "ii", "iii", "iv", "v", "vi", "vii", "viii", "ix", "x" };

        private static void RomanList(ColumnDescriptor col, IEnumerable<string> items, int startAt = 1)
        {
            int n = startAt;
            foreach (var item in items)
            {
                col.Item().PaddingTop(3).PaddingLeft(14).Row(row =>
                {
                    row.ConstantItem(20).Text($"{RomanNumerals[Math.Min(n - 1, RomanNumerals.Length - 1)]}.");
                    row.RelativeItem().Text(item);
                });
                n++;
            }
        }

        private static void SignatureLine(ColumnDescriptor col)
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Text("Name: _______________________");
                row.RelativeItem().Text("Date: _______________");
                row.RelativeItem().Text("Signature: _______________");
            });
        }

        // ── Indian-numbering amount-to-words (e.g. 420000 -> "Four Lakh, Twenty Thousand") ──

        private static readonly string[] Ones =
        {
            "", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine",
            "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen"
        };
        private static readonly string[] Tens =
        {
            "", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"
        };

        private static string TwoDigitWords(int n)
        {
            if (n < 20) return Ones[n];
            return Tens[n / 10] + (n % 10 > 0 ? " " + Ones[n % 10] : "");
        }

        private static string ThreeDigitWords(int n)
        {
            var hundreds = n / 100;
            var rest = n % 100;
            var parts = new List<string>();
            if (hundreds > 0) parts.Add(Ones[hundreds] + " Hundred");
            if (rest > 0) parts.Add(TwoDigitWords(rest));
            return string.Join(" ", parts);
        }

        public static string AmountToIndianWords(decimal amount)
        {
            long n = (long)Math.Round(amount, MidpointRounding.AwayFromZero);
            if (n <= 0) return "Zero";

            var crore = n / 10000000; n %= 10000000;
            var lakh = n / 100000; n %= 100000;
            var thousand = n / 1000; n %= 1000;
            var hundred = (int)n;

            var parts = new List<string>();
            if (crore > 0) parts.Add(TwoDigitWords((int)Math.Min(crore, 99)) + " Crore");
            if (lakh > 0) parts.Add(TwoDigitWords((int)lakh) + " Lakh");
            if (thousand > 0) parts.Add(TwoDigitWords((int)thousand) + " Thousand");
            if (hundred > 0) parts.Add(ThreeDigitWords(hundred));

            return string.Join(", ", parts);
        }
    }

    /// <summary>Static legal clause text shared by every offer letter, kept out of
    /// GeneratePdf for readability. Sourced from the company's standard template.</summary>
    internal static class ClauseText
    {
        public const string NonDisclosureIp =
            "At all times during and after your employment, you will hold in strictest confidence and not use for " +
            "your own purposes or the purposes of others or disclose anything on the intellectual property belonging " +
            "to the Company as defined in the Annexure 1 (\"Intellectual Property\"), to any person, firm, corporation " +
            "or third party, without prior authorization in writing by the Company. You agree that the results and " +
            "proceeds of your services hereunder, including, without limitation, any works of authorship resulting " +
            "from your services during your employment and any works in progress, shall be works-made-for-hire and " +
            "the Company shall be deemed the sole and exclusive owner throughout the universe in perpetuity of any " +
            "and all rights of whatsoever nature therein, whether or not now or hereafter known, existing, " +
            "contemplated, recognized or developed, with the right to use the same in perpetuity in any manner the " +
            "Company determines in its sole discretion without any further payment to you whatsoever. If, for any " +
            "reason, any of such results and proceeds shall not legally be a work-made-for-hire and/or there are any " +
            "rights which do not accrue to the Company under the foregoing provisions, then you hereby irrevocably " +
            "assign and agree to assign any and all of your right, title and interest thereto in all Intellectual " +
            "Property, including, without limitation, any and all copyrights, patents, patent applications, trade " +
            "secrets, trademarks and/or other rights of whatsoever nature therein, whether or not now or hereafter " +
            "known, existing, contemplated, recognized or developed to the Company, and the Company shall have the " +
            "right to use the same in perpetuity throughout the universe in any manner the Company may deem useful or " +
            "desirable to establish or document Company's sole and exclusive ownership of any and all rights in any " +
            "such results and proceeds, including, without limitation, the execution of appropriate copyright and/or " +
            "patent applications or assignments. To the extent that you have any rights in the results and proceeds " +
            "of your services that cannot be assigned in the manner described above, you unconditionally and " +
            "irrevocably waive the enforcement of such rights. This paragraph is subject to, and shall not be deemed " +
            "to limit, restrict, or constitute any waiver by the Company of any rights of ownership to which the " +
            "Company may be entitled by operation of law by virtue of the Company or any of its affiliates being your " +
            "employer. You hereby unconditionally and irrevocably waive all rights that you may have now or in future " +
            "with respect to any Intellectual Property / work done by you during your employment including any " +
            "rights that you may have to prevent the alteration/ translation/ destruction of any such Intellectual " +
            "Property / work. You agree that this waiver may be invoked by the Company and by any of its authorized " +
            "agents / assignees, in respect of any of Intellectual Property / work done by you during the employment " +
            "period. For the avoidance of doubt, all Intellectual Property/ work done by you during your employment " +
            "shall be for the sole benefit of the Company and its customers and shall be the exclusive property of " +
            "the Company. The Company will have sole discretion to deal with the same, and in respect of all the work " +
            "done by you during the employment together with all rights thereto, including patent and patent " +
            "application rights and copyrights, shall vest in the Company. You agree that you shall do all further " +
            "things that may be reasonably necessary or desirable in order to give full effect to the rights and " +
            "title of the Company in respect of the foregoing.";

        public const string Confidentiality =
            "In consideration of the opportunities, training and access to new techniques and know-how that will be " +
            "made available to you, you will be required to comply with the confidentiality policy of the Company. " +
            "Therefore, please ensure that you maintain as secret the confidential information as defined in Annexure " +
            "II (\"Confidential Information\") and shall not use or divulge or disclose any such Confidential " +
            "Information except as may be required under obligation of law or as may be required by the Company in " +
            "the course and scope of your employment. This covenant shall endure during the employment and shall " +
            "survive the cessation of your Contract with the Company, irrespective of the circumstances of, or the " +
            "reasons for, the cessation. In the event of any doubt regarding what constitutes Confidential " +
            "Information, you shall consult your superiors. You agree to execute and enter into a confidentiality and " +
            "assignment agreement with the Company upon the Company's request.";

        public const string CompanyPropertyPart1 =
            "Any and all memoranda, notes, records, books, other documents, art works, art assets, circular, files, " +
            "items of equipment, laptops, parts of PC of the Company, whether tangible or intangible, including all " +
            "information stored in electronic form, made or composed by you or which might be supplied/ made " +
            "available to you in connection with your work during the employment, or in any way relating to the " +
            "business or affairs of the Company, its subsidiaries, divisions, affiliates, or clients shall at all " +
            "times remain the property of the Company and shall be returned to the Company upon your ceasing to be " +
            "in the Company's employment or at any other time at the request of the Company. Further, you undertake " +
            "not to use any of the memoranda, notes, records, or other documents made or composed by you or copies " +
            "thereof, upon the termination of your Contract.";

        public const string CompanyPropertyPart2 =
            "In the event of the termination of your employment for any reason, and subject to any other provisions " +
            "hereof, the Company reserves the right, to the extent required by law, and in addition to any other " +
            "remedy the Company may have, to deduct from any monies otherwise payable to you the following: the full " +
            "amount of any specifically determined debt you owe to the Company or any of its affiliates at the time " +
            "of or subsequent to the termination of your employment with the Company and (ii) The value of Company's " +
            "property which you retain in your possession after the termination of your employment with the Company " +
            "following Company's written request for such item(s) return and your failure to return such items " +
            "within ten (10) days of receiving such notice. In the event that the law of any state or other " +
            "jurisdiction requires the consent of an employee for such deductions, this Letter shall serve as such consent.";

        public const string NoticeOfChange =
            "Any change in your personal information including residential address, marital status, number of " +
            "children and education qualification should be notified to the Company in writing within 7 days. Any " +
            "notice required to be given to you shall be deemed to have been duly and properly given if delivered to " +
            "you personally or sent by post at your address as recorded in the Company's records. You agree to keep " +
            "the Company informed in writing of any change in your address.";

        public const string Exclusivity =
            "Unless prior written agreement is given to you by the Company, you agree to work exclusively for the " +
            "Company, within the context of the responsibilities defined above, and not to accept or perform any " +
            "other paid/ unpaid employment or consulting in addition to this, even temporary. You agree, represent " +
            "and warrant to the Company that (i) you are not subject to/party to any covenants, agreements or " +
            "restrictions, including, without limitation, any covenants, agreements or restrictions arising out of " +
            "any prior contracts or independent contractor relationships, which would be breached or violated by " +
            "your execution of this Contract or performance of your duties hereunder; (ii) there is no order of any " +
            "court or other authority disqualifying you for appointment under this contract; and (iii) you are not " +
            "financially interested in any other person, firm or corporation engaged in the production, distribution " +
            "or exhibition of motion pictures or television programs or the animation business (including, without " +
            "limitation, motion pictures, television programming produced for, distributed to or exhibited on free, " +
            "cable, pay, satellite and/or subscription television, music and/or interactive), anywhere in the world.";

        public const string NonCompeteIntro = "Non-Competition.";
        public const string NonCompete =
            "You covenant and agree that, during the term of your employment with the Company and for twelve months " +
            "after the termination thereof, regardless of the reason for the employment termination, you will not, " +
            "directly or indirectly, anywhere in the Territory, on behalf of any Competitive Business perform the " +
            "same or substantially the same Job Duties, which is directly in competition with the company.";

        public const string NonSolicitCustomers = "Non-Solicitation of Customers, Customer Prospects, and Vendors.";
        public const string NonSolicitCustomersBody =
            "You also covenant and agree that during the term of your employment with the Company and for twelve " +
            "months after the termination thereof, regardless of the reason for the employment termination, you will " +
            "not, directly or indirectly, solicit or attempt to solicit any business from any of the Company's " +
            "Customers, Customer Prospects, or Vendors with whom you had Material Contact during the last two years " +
            "of your employment with the Company.";

        public const string NonSolicitEmployees = "Non-Solicitation of Employees.";
        public const string NonSolicitEmployeesBody =
            "You also covenant and agree that during the term of your employment with the Company and for twelve " +
            "months after the termination thereof, regardless of the reason for the employment termination, you will " +
            "not, directly or indirectly, on your own behalf or on behalf of or in conjunction with any person or " +
            "legal entity, recruit, solicit, or induce, or attempt to recruit, solicit, or induce, any non-clerical " +
            "employee of the Company with whom you had personal contact or supervised while performing your Job " +
            "Duties, to terminate their employment relationship with the Company.";

        public const string Jurisdiction =
            "The jurisdiction concerning this contract will be with the courts in Telangana which you undertake to " +
            "not contest. The contract shall be governed by and interpreted in accordance with the laws of India.";

        public const string AnnexureIIntro =
            "The term 'Intellectual' or 'Company owned property' as it is used in this schedule, shall include, but " +
            "not be limited to the following:";

        public const string AnnexureIItem1 =
            "Business or financial records, strategies, patents, patent applications, trademarks, trade secrets, " +
            "forecasts, budgets projections, Licenses, prices of products and services, Clients list, Goodwill, " +
            "Personnel information's, and other information regarding formulas, patterns, complications, programs.";

        public const string AnnexureIItem2 =
            "All work created and/or developed by you solely or a team during your work hours on our premises and/or " +
            "using Company labour resources, equipment, software and/or facilities shall be deemed to be Company's " +
            "intellectual property and that the said work is deemed to be owned by the Company. (The work includes " +
            "but not limited to the following items and all other rights related to your work; patents; patent " +
            "applications; trademarks; trade secrets; copyrights; transparencies; materials; inventions; " +
            "improvements; reports; techniques; discovery; methods; processes; models; mock-ups; miniatures; job " +
            "notes; storyboards; special effects; animations; modelling, designs; technology; know-how; software " +
            "programs; concepts for programs; object descriptions; art assets; art work; paintings; drawing; " +
            "sketching; model work; ideas or information made and practiced by the Company.)";

        public const string AnnexureIItem3 =
            "All materials provided to or created by you solely or as a part of team during your work hours on our " +
            "premises is deemed to be owned by the Company; (The Materials includes but not limited to the " +
            "following: all motion pictures; films; tapes; Cassettes; cables and otherwise, software programs; all " +
            "software and hardware related to any interactive devices; storage medium such as CD-ROM; CD; floppy or " +
            "similar disc system, interactive cable, fiber optic; interactive telephone and any other devices or " +
            "methods now known or later created or any other computer based system. Instruments required for drawing " +
            "and painting. The materials also include by way of illustration only sequel and remake, art characters, " +
            "any publications; literature; training material, books; documents.)";

        public const string AnnexureIIIntro =
            "\"Confidential Information\" includes but is not limited to information which is or fairly can be " +
            "considered to be of a confidential nature, which is obtained whether (without limitation) in graphic, " +
            "written, electronic or machine readable form on any media, by you; and whether or not the information " +
            "is expressly stated to be confidential or marked as such, in writing (provided that the confidentiality " +
            "of such information is reasonably apparent), and also includes all Intellectual Property (as defined in " +
            "Annexure I), but is not limited to:";
    }
}
